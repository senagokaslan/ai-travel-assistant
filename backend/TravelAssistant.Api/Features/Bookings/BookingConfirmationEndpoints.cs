using System.Data;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using TravelAssistant.Api.Features.Auth;
using TravelAssistant.Api.Features.Flights;
using TravelAssistant.Api.Features.Hotels;

namespace TravelAssistant.Api.Features.Bookings;

internal static class BookingConfirmationEndpoints
{
    public static IEndpointRouteBuilder MapBookingConfirmationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/bookings/confirm/hotel", ConfirmHotelAsync);
        app.MapPost("/api/bookings/confirm/flight", ConfirmFlightAsync);
        return app;
    }

    private static async Task<IResult> ConfirmHotelAsync(HotelBookingConfirmationRequest request, HttpRequest httpRequest, IConfiguration configuration, SessionStore sessions, CancellationToken cancellationToken)
    {
        var user = await sessions.GetValidUserAsync(httpRequest, configuration, cancellationToken);
        if (user is null) return Results.Unauthorized();
        if (request.RequestKey == Guid.Empty || request.Details?.Selection is null) return Invalid("Son onay isteği eksik veya geçersiz.");
        var selection = request.Details.Selection;
        var selectionError = ValidateHotelSelection(selection);
        if (selectionError is not null) return Invalid(selectionError);
        var people = BookingPeopleValidator.Validate(request.Details.Travelers, request.Details.Contact, selection.Adults, selection.Children, 0, selection.ChildAges, "hotel");
        if (!people.IsValid) return Invalid(people.Error!, people.Field);
        if (!TryParseRoomSelection(selection.OptionKey, selection.Rooms, out var requestedRooms)) return Invalid("Oda seçeneği geçersiz.");

        await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Postgres"));
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await LockRequestAsync(connection, user.Id, request.RequestKey, cancellationToken);
        var existing = await FindExistingAsync(connection, user.Id, request.RequestKey, cancellationToken);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(ToResponse(existing, false));
        }

        var roomIds = requestedRooms.Keys.Order().ToArray();
        var lockedRoomCount = 0;
        await using (var lockRooms = new NpgsqlCommand("SELECT id FROM hotel_rooms WHERE hotel_id=@hotel_id AND is_active AND id=ANY(@room_ids) ORDER BY id FOR UPDATE", connection))
        {
            lockRooms.Parameters.AddWithValue("hotel_id", selection.HotelId);
            lockRooms.Parameters.AddWithValue("room_ids", roomIds);
            await using var reader = await lockRooms.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) lockedRoomCount++;
        }
        if (lockedRoomCount != roomIds.Length)
            return await RollbackConflictAsync(transaction, "inventory_unavailable", "Seçilen oda artık satışta değil.", cancellationToken);

        var lockedRateCount = 0;
        await using (var lockRates = new NpgsqlCommand("SELECT room_id, stay_date FROM room_daily_rates WHERE room_id=ANY(@room_ids) AND stay_date>=@check_in AND stay_date<@check_out ORDER BY room_id, stay_date FOR UPDATE", connection))
        {
            lockRates.Parameters.AddWithValue("room_ids", roomIds);
            lockRates.Parameters.AddWithValue("check_in", selection.CheckIn);
            lockRates.Parameters.AddWithValue("check_out", selection.CheckOut);
            await using var reader = await lockRates.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) lockedRateCount++;
        }
        var expectedRateCount = roomIds.Length * (selection.CheckOut.DayNumber - selection.CheckIn.DayNumber);
        if (lockedRateCount != expectedRateCount)
            return await RollbackConflictAsync(transaction, "inventory_unavailable", "Seçilen konaklama için fiyat veya stok kaydı artık bulunmuyor.", cancellationToken);

        var hotel = await HotelAvailabilityService.GetByIdAsync(connection, selection.HotelId, selection.CheckIn, selection.CheckOut, selection.Rooms, selection.Adults + selection.Children, cancellationToken);
        var option = hotel?.Options.SingleOrDefault(candidate => candidate.Key == selection.OptionKey);
        if (option is null) return await RollbackConflictAsync(transaction, "inventory_unavailable", "Seçilen tarihlerde yeterli oda kalmadı.", cancellationToken);
        if (selection.QuotedTotal is not > 0 || selection.QuotedTotal != option.TotalPrice || !string.Equals(selection.QuotedCurrency, "TRY", StringComparison.OrdinalIgnoreCase))
            return await RollbackPriceAsync(transaction, option.TotalPrice, "TRY", cancellationToken);

        var booking = await InsertBookingAsync(
            connection,
            user.Id,
            request.RequestKey,
            "hotel",
            hotel!.Name,
            option.TotalPrice,
            "TRY",
            new { selection, people = people.Value },
            cancellationToken);

        foreach (var room in option.Rooms.OrderBy(item => item.RoomId))
        {
            await using var hold = new NpgsqlCommand("INSERT INTO hotel_room_holds (booking_id, room_id, check_in, check_out, quantity, status, held_until) VALUES (@booking_id, @room_id, @check_in, @check_out, @quantity, 'confirmed', NULL)", connection);
            hold.Parameters.AddWithValue("booking_id", booking.Id);
            hold.Parameters.AddWithValue("room_id", room.RoomId);
            hold.Parameters.AddWithValue("check_in", selection.CheckIn);
            hold.Parameters.AddWithValue("check_out", selection.CheckOut);
            hold.Parameters.AddWithValue("quantity", room.Quantity);
            await hold.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(ToResponse(booking, true));
    }

    private static async Task<IResult> ConfirmFlightAsync(FlightBookingConfirmationRequest request, HttpRequest httpRequest, IConfiguration configuration, SessionStore sessions, CancellationToken cancellationToken)
    {
        var user = await sessions.GetValidUserAsync(httpRequest, configuration, cancellationToken);
        if (user is null) return Results.Unauthorized();
        if (request.RequestKey == Guid.Empty || request.Details?.Selection is null) return Invalid("Son onay isteği eksik veya geçersiz.");
        var selection = request.Details.Selection;
        var selectionError = ValidateFlightSelection(selection);
        if (selectionError is not null) return Invalid(selectionError);
        var tripType = (selection.TripType ?? "").Trim().ToLowerInvariant();
        var people = BookingPeopleValidator.Validate(request.Details.Travelers, request.Details.Contact, selection.Adults, selection.Children, selection.Infants, null, "flight");
        if (!people.IsValid) return Invalid(people.Error!, people.Field);

        var fareIds = new[] { selection.OutboundFareId }.Concat(selection.InboundFareId is null ? Array.Empty<Guid>() : new[] { selection.InboundFareId.Value }).Distinct().Order().ToArray();
        var seatedPassengers = selection.Adults + selection.Children;
        await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Postgres"));
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await LockRequestAsync(connection, user.Id, request.RequestKey, cancellationToken);
        var existing = await FindExistingAsync(connection, user.Id, request.RequestKey, cancellationToken);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(ToResponse(existing, false));
        }

        var flightIds = new List<Guid>();
        await using (var lockFares = new NpgsqlCommand("SELECT id, flight_id FROM flight_fares WHERE id=ANY(@fare_ids) ORDER BY id FOR UPDATE", connection))
        {
            lockFares.Parameters.AddWithValue("fare_ids", fareIds);
            await using var reader = await lockFares.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) flightIds.Add(reader.GetGuid(1));
        }
        if (flightIds.Count != fareIds.Length) return await RollbackConflictAsync(transaction, "inventory_unavailable", "Seçilen bilet artık satışta değil.", cancellationToken);
        var lockedFlightIds = flightIds.Distinct().Order().ToArray();
        await LockFlightsAndLegsAsync(connection, lockedFlightIds, cancellationToken);

        var origin = (selection.From ?? "").Trim().ToUpperInvariant();
        var destination = (selection.To ?? "").Trim().ToUpperInvariant();
        var outboundOptions = await FlightSearchService.SearchAsync(connection, origin, destination, selection.DepartureDate, seatedPassengers, cancellationToken);
        var outbound = outboundOptions.SingleOrDefault(option => option.Fare.Id == selection.OutboundFareId);
        if (outbound is null) return await RollbackConflictAsync(transaction, "inventory_unavailable", "Gidiş uçuşunda yeterli koltuk kalmadı veya sefer iptal edildi.", cancellationToken);
        FlightItinerary? inbound = null;
        if (tripType == "round-trip")
        {
            var inboundOptions = await FlightSearchService.SearchAsync(connection, destination, origin, selection.ReturnDate!.Value, seatedPassengers, cancellationToken);
            inbound = inboundOptions.SingleOrDefault(option => option.Fare.Id == selection.InboundFareId);
            if (inbound is null) return await RollbackConflictAsync(transaction, "inventory_unavailable", "Dönüş uçuşunda yeterli koltuk kalmadı veya sefer iptal edildi.", cancellationToken);
            if (inbound.Fare.Currency != outbound.Fare.Currency) return await RollbackConflictAsync(transaction, "selection_changed", "Gidiş ve dönüş para birimleri artık eşleşmiyor.", cancellationToken);
        }

        var totalPrice = (outbound.Fare.Price + (inbound?.Fare.Price ?? 0)) * (selection.Adults + selection.Children + selection.Infants);
        if (selection.QuotedTotal is not > 0 || selection.QuotedTotal != totalPrice || !string.Equals(selection.QuotedCurrency, outbound.Fare.Currency, StringComparison.OrdinalIgnoreCase))
            return await RollbackPriceAsync(transaction, totalPrice, outbound.Fare.Currency, cancellationToken);

        var title = $"{origin} → {destination}";
        var booking = await InsertBookingAsync(
            connection,
            user.Id,
            request.RequestKey,
            "flight",
            title,
            totalPrice,
            outbound.Fare.Currency,
            new { selection, people = people.Value, journey = new { outbound, inbound } },
            cancellationToken);

        foreach (var itinerary in new[] { outbound, inbound }.Where(item => item is not null).Select(item => item!))
        {
            await using (var updateFare = new NpgsqlCommand("UPDATE flight_fares SET seats_available=seats_available-@seats WHERE id=@fare_id AND is_active AND seats_available>=@seats", connection))
            {
                updateFare.Parameters.AddWithValue("fare_id", itinerary.Fare.Id);
                updateFare.Parameters.AddWithValue("seats", seatedPassengers);
                if (await updateFare.ExecuteNonQueryAsync(cancellationToken) != 1) return await RollbackConflictAsync(transaction, "inventory_unavailable", "Bilet sınıfında yeterli koltuk kalmadı.", cancellationToken);
            }
            await using (var updateLegs = new NpgsqlCommand("UPDATE flight_legs SET seats_available=seats_available-@seats WHERE flight_id=@flight_id AND seats_available>=@seats", connection))
            {
                updateLegs.Parameters.AddWithValue("flight_id", itinerary.Id);
                updateLegs.Parameters.AddWithValue("seats", seatedPassengers);
                if (await updateLegs.ExecuteNonQueryAsync(cancellationToken) != itinerary.Segments.Count) return await RollbackConflictAsync(transaction, "inventory_unavailable", "Uçuş parçalarından birinde yeterli koltuk kalmadı.", cancellationToken);
            }
            await using var allocation = new NpgsqlCommand("INSERT INTO flight_booking_allocations (booking_id, fare_id, seats) VALUES (@booking_id, @fare_id, @seats)", connection);
            allocation.Parameters.AddWithValue("booking_id", booking.Id);
            allocation.Parameters.AddWithValue("fare_id", itinerary.Fare.Id);
            allocation.Parameters.AddWithValue("seats", seatedPassengers);
            await allocation.ExecuteNonQueryAsync(cancellationToken);
            foreach (var segment in itinerary.Segments)
            {
                await using var legAllocation = new NpgsqlCommand("INSERT INTO flight_booking_leg_allocations (booking_id, leg_id, seats) VALUES (@booking_id, @leg_id, @seats)", connection);
                legAllocation.Parameters.AddWithValue("booking_id", booking.Id);
                legAllocation.Parameters.AddWithValue("leg_id", segment.Id);
                legAllocation.Parameters.AddWithValue("seats", seatedPassengers);
                await legAllocation.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(ToResponse(booking, true));
    }

    private static string? ValidateHotelSelection(HotelBookingSummaryRequest selection)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (selection.HotelId == Guid.Empty || string.IsNullOrWhiteSpace(selection.OptionKey) || selection.OptionKey.Length > 1_000) return "Otel seçimi geçersiz.";
        if (selection.CheckIn < today || selection.CheckOut <= selection.CheckIn || selection.CheckOut.DayNumber - selection.CheckIn.DayNumber > 30) return "Konaklama tarihleri geçersiz.";
        if (selection.Rooms is < 1 or > 8 || selection.Adults is < 1 or > 20 || selection.Children is < 0 or > 8 || selection.Adults < selection.Rooms) return "Oda veya misafir sayıları geçersiz.";
        if (selection.ChildAges is null || selection.ChildAges.Length != selection.Children || selection.ChildAges.Any(age => age is < 0 or > 17)) return "Çocuk yaşları geçersiz.";
        return null;
    }

    private static string? ValidateFlightSelection(FlightBookingSummaryRequest selection)
    {
        var origin = (selection.From ?? "").Trim().ToUpperInvariant();
        var destination = (selection.To ?? "").Trim().ToUpperInvariant();
        var tripType = (selection.TripType ?? "").Trim().ToLowerInvariant();
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (selection.OutboundFareId == Guid.Empty || origin.Length != 3 || destination.Length != 3 || origin == destination) return "Uçuş seçimi veya rota geçersiz.";
        if (selection.DepartureDate < today || tripType is not ("one-way" or "round-trip")) return "Uçuş tarihi veya yolculuk türü geçersiz.";
        if (selection.Adults is < 1 or > 9 || selection.Children is < 0 or > 8 || selection.Infants is < 0 or > 9 || selection.Infants > selection.Adults || selection.Adults + selection.Children + selection.Infants > 20) return "Yolcu sayıları geçersiz.";
        if (tripType == "round-trip" && (selection.InboundFareId is null || selection.ReturnDate is null || selection.ReturnDate < selection.DepartureDate)) return "Dönüş uçuşu seçimi geçersiz.";
        if (tripType == "one-way" && (selection.InboundFareId is not null || selection.ReturnDate is not null)) return "Tek yön uçuşunda dönüş seçimi bulunamaz.";
        return null;
    }

    private static bool TryParseRoomSelection(string optionKey, int expectedRooms, out Dictionary<Guid, int> rooms)
    {
        rooms = new Dictionary<Guid, int>();
        foreach (var part in optionKey.Split('-', StringSplitOptions.RemoveEmptyEntries))
        {
            var values = part.Split(':', StringSplitOptions.TrimEntries);
            if (values.Length != 2 || !Guid.TryParseExact(values[0], "N", out var roomId) || !int.TryParse(values[1], out var quantity) || quantity < 1 || !rooms.TryAdd(roomId, quantity)) return false;
        }
        return rooms.Count > 0 && rooms.Values.Sum() == expectedRooms;
    }

    private static async Task LockRequestAsync(NpgsqlConnection connection, Guid userId, Guid requestKey, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@lock_key, 0))", connection);
        command.Parameters.AddWithValue("lock_key", $"booking:{userId:N}:{requestKey:N}");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task LockFlightsAndLegsAsync(NpgsqlConnection connection, Guid[] flightIds, CancellationToken cancellationToken)
    {
        await using (var flights = new NpgsqlCommand("SELECT id FROM flights WHERE id=ANY(@flight_ids) ORDER BY id FOR UPDATE", connection))
        {
            flights.Parameters.AddWithValue("flight_ids", flightIds);
            await flights.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var legs = new NpgsqlCommand("SELECT id FROM flight_legs WHERE flight_id=ANY(@flight_ids) ORDER BY id FOR UPDATE", connection))
        {
            legs.Parameters.AddWithValue("flight_ids", flightIds);
            await legs.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<ConfirmedBooking?> FindExistingAsync(NpgsqlConnection connection, Guid userId, Guid requestKey, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT id, reference_code, kind, title, total_price, BTRIM(currency) FROM app_bookings WHERE user_id=@user_id AND request_key=@request_key", connection);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("request_key", requestKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new ConfirmedBooking(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetDecimal(4), reader.GetString(5))
            : null;
    }

    private static async Task<ConfirmedBooking> InsertBookingAsync(NpgsqlConnection connection, Guid userId, Guid requestKey, string kind, string title, decimal totalPrice, string currency, object details, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var reference = $"SIM-{id:N}".ToUpperInvariant();
        await using var command = new NpgsqlCommand("INSERT INTO app_bookings (id, user_id, request_key, reference_code, kind, title, status, total_price, currency, details, confirmed_at) VALUES (@id, @user_id, @request_key, @reference, @kind, @title, 'simulated', @total_price, @currency, @details, now())", connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("request_key", requestKey);
        command.Parameters.AddWithValue("reference", reference);
        command.Parameters.AddWithValue("kind", kind);
        command.Parameters.AddWithValue("title", title);
        command.Parameters.AddWithValue("total_price", totalPrice);
        command.Parameters.AddWithValue("currency", currency);
        command.Parameters.AddWithValue("details", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(details));
        await command.ExecuteNonQueryAsync(cancellationToken);
        return new ConfirmedBooking(id, reference, kind, title, totalPrice, currency);
    }

    private static object ToResponse(ConfirmedBooking booking, bool created) => new
    {
        created,
        reservationCreated = true,
        booking.Id,
        booking.ReferenceCode,
        booking.Kind,
        booking.Title,
        booking.TotalPrice,
        booking.Currency,
        message = created ? "Rezervasyon simülasyonu oluşturuldu." : "Bu onay daha önce işlendi; mevcut rezervasyon simülasyonu döndürüldü."
    };

    private static async Task<IResult> RollbackConflictAsync(NpgsqlTransaction transaction, string code, string message, CancellationToken cancellationToken)
    {
        await transaction.RollbackAsync(cancellationToken);
        return Results.Conflict(new { code, message });
    }

    private static async Task<IResult> RollbackPriceAsync(NpgsqlTransaction transaction, decimal currentTotal, string currency, CancellationToken cancellationToken)
    {
        await transaction.RollbackAsync(cancellationToken);
        return Results.Conflict(new { code = "price_changed", message = "Fiyat son kontrolde değişti. Yeni fiyatı kabul etmeden rezervasyon oluşturulmadı.", currentTotal, currency });
    }

    private static IResult Invalid(string message, string? field = null) => Results.BadRequest(new { code = "invalid_confirmation", field, message });
    private sealed record ConfirmedBooking(Guid Id, string ReferenceCode, string Kind, string Title, decimal TotalPrice, string Currency);
}
