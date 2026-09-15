using Npgsql;

namespace TravelAssistant.Api.Features.Hotels;

static class HotelAvailabilityService
{
    public static async Task<IReadOnlyList<HotelAvailabilityResult>> SearchAsync(
        NpgsqlConnection connection,
        string term,
        DateOnly checkIn,
        DateOnly checkOut,
        int requestedRooms,
        int guests,
        CancellationToken cancellationToken) =>
        await SearchCoreAsync(connection, term, null, checkIn, checkOut, requestedRooms, guests, cancellationToken);

    public static async Task<HotelAvailabilityResult?> GetByIdAsync(
        NpgsqlConnection connection,
        Guid hotelId,
        DateOnly checkIn,
        DateOnly checkOut,
        int requestedRooms,
        int guests,
        CancellationToken cancellationToken) =>
        (await SearchCoreAsync(connection, null, hotelId, checkIn, checkOut, requestedRooms, guests, cancellationToken)).SingleOrDefault();

    private static async Task<IReadOnlyList<HotelAvailabilityResult>> SearchCoreAsync(
        NpgsqlConnection connection,
        string? term,
        Guid? hotelId,
        DateOnly checkIn,
        DateOnly checkOut,
        int requestedRooms,
        int guests,
        CancellationToken cancellationToken)
    {
        var predicate = hotelId.HasValue
            ? "h.id = @hotel_id"
            : "(h.name ILIKE @like OR c.name ILIKE @like OR h.district ILIKE @like)";
        var sql = $$"""
            SELECT h.id, h.name, c.name, h.district, h.stars, h.rating, h.description, h.board_types,
                   r.id, r.name, r.capacity, r.features,
                   rr.stay_date, rr.nightly_price,
                   GREATEST(rr.rooms_available - COALESCE((
                       SELECT SUM(rh.quantity)
                       FROM hotel_room_holds rh
                       WHERE rh.room_id = r.id
                         AND rh.status IN ('held', 'confirmed')
                         AND rh.check_in <= rr.stay_date
                         AND rh.check_out > rr.stay_date
                         AND (rh.status = 'confirmed' OR rh.held_until IS NULL OR rh.held_until > now())
                   ), 0), 0) AS available_after_holds
            FROM hotels h
            JOIN travel_cities c ON c.id = h.city_id
            LEFT JOIN hotel_rooms r ON r.hotel_id = h.id AND r.is_active
            LEFT JOIN room_daily_rates rr ON rr.room_id = r.id
                 AND rr.stay_date >= @check_in AND rr.stay_date < @check_out
            WHERE h.is_active
              AND {{predicate}}
            ORDER BY h.rating DESC, h.id, r.capacity, r.id, rr.stay_date
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        if (hotelId.HasValue) command.Parameters.AddWithValue("hotel_id", hotelId.Value);
        else command.Parameters.AddWithValue("like", $"%{term!.Trim()}%");
        command.Parameters.AddWithValue("check_in", checkIn.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("check_out", checkOut.ToDateTime(TimeOnly.MinValue));

        var hotels = new Dictionary<Guid, HotelAccumulator>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var rowHotelId = reader.GetGuid(0);
            if (!hotels.TryGetValue(rowHotelId, out var hotel))
            {
                hotel = new HotelAccumulator(
                    rowHotelId, reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.GetInt16(4), reader.GetDecimal(5), reader.GetString(6), reader.GetFieldValue<string[]>(7));
                hotels.Add(rowHotelId, hotel);
            }

            if (reader.IsDBNull(8)) continue;
            var roomId = reader.GetGuid(8);
            if (!hotel.Rooms.TryGetValue(roomId, out var room))
            {
                room = new RoomAccumulator(roomId, reader.GetString(9), reader.GetInt16(10), reader.GetFieldValue<string[]>(11));
                hotel.Rooms.Add(roomId, room);
            }

            if (!reader.IsDBNull(12))
            {
                room.Nights.Add(new RoomNight(
                    reader.GetFieldValue<DateOnly>(12),
                    reader.GetDecimal(13),
                    checked((int)reader.GetInt64(14))));
            }
        }

        return hotels.Values
            .Select(hotel => Calculate(hotel, checkIn, checkOut, requestedRooms, guests))
            .Where(result => result is not null)
            .Select(result => result!)
            .OrderBy(result => result.TotalPrice)
            .ThenByDescending(result => result.Rating)
            .ToArray();
    }

    private static HotelAvailabilityResult? Calculate(
        HotelAccumulator hotel,
        DateOnly checkIn,
        DateOnly checkOut,
        int requestedRooms,
        int guests)
    {
        var expectedNights = Enumerable.Range(0, checkOut.DayNumber - checkIn.DayNumber)
            .Select(checkIn.AddDays)
            .ToHashSet();

        var rooms = hotel.Rooms.Values
            .Select(room => ToAvailableRoom(room, expectedNights))
            .Where(room => room is not null)
            .Select(room => room!)
            .OrderBy(room => room.StayPrice)
            .ThenBy(room => room.Capacity)
            .ToArray();

        if (rooms.Length == 0) return null;

        var candidates = new List<RoomCombination>();
        BuildCombinations(rooms, 0, requestedRooms, guests, new List<SelectedRoom>(), candidates);
        var options = candidates
            .OrderBy(candidate => candidate.TotalPrice)
            .ThenBy(candidate => candidate.TotalCapacity)
            .ThenBy(candidate => candidate.Rooms.Count)
            .Take(50)
            .Select(candidate => new HotelRoomOption(
                string.Join("-", candidate.Rooms.OrderBy(room => room.Room.Id).Select(room => $"{room.Room.Id:N}:{room.Quantity}")),
                candidate.TotalPrice,
                candidate.TotalCapacity,
                candidate.Rooms.Select(selected => new HotelRoomSelection(
                    selected.Room.Id,
                    selected.Room.Name,
                    selected.Quantity,
                    selected.Room.Capacity,
                    selected.Room.Features,
                    selected.Room.StayPrice,
                    selected.Room.StayPrice * selected.Quantity,
                    selected.Room.Nights.Select(night => new HotelNightPrice(night.Date, night.Price, night.Available)).ToArray()
                )).ToArray()
            )).ToArray();

        if (options.Length == 0) return null;
        return new HotelAvailabilityResult(
            hotel.Id, hotel.Name, hotel.City, hotel.District, hotel.Stars, hotel.Rating, hotel.Description,
            hotel.BoardTypes,
            options.SelectMany(option => option.Rooms).Select(room => room.Name).Distinct().ToArray(),
            options.SelectMany(option => option.Rooms).SelectMany(room => room.Features).Distinct().ToArray(),
            options[0].TotalPrice,
            options);
    }

    private static AvailableRoom? ToAvailableRoom(RoomAccumulator room, HashSet<DateOnly> expectedNights)
    {
        var nights = room.Nights
            .GroupBy(night => night.Date)
            .Select(group => group.Single())
            .OrderBy(night => night.Date)
            .ToArray();
        if (nights.Length != expectedNights.Count || nights.Any(night => !expectedNights.Contains(night.Date) || night.Available < 1)) return null;
        return new AvailableRoom(room.Id, room.Name, room.Capacity, room.Features, nights.Min(night => night.Available), nights.Sum(night => night.Price), nights);
    }

    private static void BuildCombinations(
        AvailableRoom[] roomTypes,
        int index,
        int roomsLeft,
        int guests,
        List<SelectedRoom> selected,
        List<RoomCombination> candidates)
    {
        if (roomsLeft == 0)
        {
            var capacity = selected.Sum(item => item.Room.Capacity * item.Quantity);
            if (capacity >= guests)
            {
                candidates.Add(new RoomCombination(
                    selected.ToArray(),
                    capacity,
                    selected.Sum(item => item.Room.StayPrice * item.Quantity)));
            }
            return;
        }
        if (index >= roomTypes.Length || candidates.Count >= 10_000) return;

        var room = roomTypes[index];
        var maximum = Math.Min(room.MinimumAvailable, roomsLeft);
        for (var quantity = maximum; quantity >= 0; quantity--)
        {
            if (quantity > 0) selected.Add(new SelectedRoom(room, quantity));
            BuildCombinations(roomTypes, index + 1, roomsLeft - quantity, guests, selected, candidates);
            if (quantity > 0) selected.RemoveAt(selected.Count - 1);
        }
    }

    private sealed record RoomNight(DateOnly Date, decimal Price, int Available);
    private sealed record AvailableRoom(Guid Id, string Name, int Capacity, string[] Features, int MinimumAvailable, decimal StayPrice, RoomNight[] Nights);
    private sealed record SelectedRoom(AvailableRoom Room, int Quantity);
    private sealed record RoomCombination(IReadOnlyList<SelectedRoom> Rooms, int TotalCapacity, decimal TotalPrice);
    private sealed record HotelAccumulator(Guid Id, string Name, string City, string District, int Stars, decimal Rating, string Description, string[] BoardTypes)
    {
        public Dictionary<Guid, RoomAccumulator> Rooms { get; } = new();
    }
    private sealed record RoomAccumulator(Guid Id, string Name, int Capacity, string[] Features)
    {
        public List<RoomNight> Nights { get; } = new();
    }
}

record HotelAvailabilityResult(
    Guid Id,
    string Name,
    string City,
    string District,
    int Stars,
    decimal Rating,
    string Description,
    string[] BoardTypes,
    string[] Rooms,
    string[] Features,
    decimal TotalPrice,
    HotelRoomOption[] Options);

record HotelRoomOption(string Key, decimal TotalPrice, int TotalCapacity, HotelRoomSelection[] Rooms);
record HotelRoomSelection(Guid RoomId, string Name, int Quantity, int Capacity, string[] Features, decimal NightlyTotal, decimal LineTotal, HotelNightPrice[] Nights);
record HotelNightPrice(DateOnly Date, decimal Price, int Available);
