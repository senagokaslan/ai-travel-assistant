using System.Data;
using System.Text.Json;
using Npgsql;
using TravelAssistant.Api.Features.Auth;

namespace TravelAssistant.Api.Features.Bookings;

internal static class BookingManagementEndpoints
{
    public static IEndpointRouteBuilder MapBookingManagementEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/bookings", ListAsync);
        app.MapGet("/api/bookings/{id:guid}", GetAsync);
        app.MapPost("/api/bookings/{id:guid}/cancel", CancelAsync);
        return app;
    }

    private static async Task<IResult> ListAsync(HttpRequest request, IConfiguration configuration, SessionStore sessions, CancellationToken cancellationToken)
    {
        if (!sessions.TryGet(request, out var user)) return Results.Unauthorized();
        await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Postgres"));
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT id, reference_code, kind, title, status, total_price, BTRIM(currency), details::text, created_at, confirmed_at, cancelled_at FROM app_bookings WHERE user_id=@user_id ORDER BY created_at DESC, id DESC",
            connection);
        command.Parameters.AddWithValue("user_id", user.Id);
        var rows = new List<BookingRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) rows.Add(ReadRecord(reader));
        var today = DateOnly.FromDateTime(DateTime.Now);
        return Results.Ok(rows.Select(row => ToListItem(row, today)));
    }

    private static async Task<IResult> GetAsync(Guid id, HttpRequest request, IConfiguration configuration, SessionStore sessions, CancellationToken cancellationToken)
    {
        if (!sessions.TryGet(request, out var user)) return Results.Unauthorized();
        await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Postgres"));
        await connection.OpenAsync(cancellationToken);
        var booking = await FindOwnedAsync(connection, user.Id, id, false, cancellationToken);
        return booking is null
            ? Results.NotFound(new { code = "booking_not_found", message = "Rezervasyon bulunamadı veya bu hesaba ait değil." })
            : Results.Ok(ToDetail(booking, DateOnly.FromDateTime(DateTime.Now)));
    }

    private static async Task<IResult> CancelAsync(Guid id, BookingCancellationRequest body, HttpRequest request, IConfiguration configuration, SessionStore sessions, CancellationToken cancellationToken)
    {
        if (!sessions.TryGet(request, out var user)) return Results.Unauthorized();
        if (body is null || !body.Confirmed) return Results.BadRequest(new { code = "cancellation_confirmation_required", message = "İptal işlemi için açık onay vermelisiniz." });

        await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Postgres"));
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var booking = await FindOwnedAsync(connection, user.Id, id, true, cancellationToken);
        if (booking is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Results.NotFound(new { code = "booking_not_found", message = "Rezervasyon bulunamadı veya bu hesaba ait değil." });
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        var travel = ReadTravel(booking);
        if (string.Equals(booking.Status, "cancelled", StringComparison.OrdinalIgnoreCase))
            return await RollbackConflictAsync(transaction, "already_cancelled", "Bu rezervasyon daha önce iptal edilmiş; stok ikinci kez değiştirilmedi.", cancellationToken);
        if (!string.Equals(booking.Status, "simulated", StringComparison.OrdinalIgnoreCase))
            return await RollbackConflictAsync(transaction, "cancellation_not_allowed", "Bu rezervasyonun mevcut durumu iptale uygun değil.", cancellationToken);
        if (travel.StartDate is null || travel.StartDate <= today)
            return await RollbackConflictAsync(transaction, "cancellation_period_closed", "Başlamış veya geçmiş tarihli rezervasyonlar iptal edilemez.", cancellationToken);

        if (booking.Kind == "hotel")
        {
            await using var release = new NpgsqlCommand("UPDATE hotel_room_holds SET status='released', held_until=NULL WHERE booking_id=@booking_id AND status IN ('held', 'confirmed')", connection);
            release.Parameters.AddWithValue("booking_id", booking.Id);
            await release.ExecuteNonQueryAsync(cancellationToken);
        }
        else if (booking.Kind == "flight")
        {
            var fareAllocations = new List<FareAllocation>();
            await using (var lockFares = new NpgsqlCommand("SELECT ff.id, fba.seats FROM flight_booking_allocations fba JOIN flight_fares ff ON ff.id=fba.fare_id WHERE fba.booking_id=@booking_id ORDER BY ff.id FOR UPDATE OF ff", connection))
            {
                lockFares.Parameters.AddWithValue("booking_id", booking.Id);
                await using var reader = await lockFares.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken)) fareAllocations.Add(new FareAllocation(reader.GetGuid(0), reader.GetInt32(1)));
            }
            var legAllocations = new List<LegAllocation>();
            await using (var lockLegs = new NpgsqlCommand("SELECT leg.id, allocation.seats FROM flight_booking_leg_allocations allocation JOIN flight_legs leg ON leg.id=allocation.leg_id WHERE allocation.booking_id=@booking_id ORDER BY leg.id FOR UPDATE OF leg", connection))
            {
                lockLegs.Parameters.AddWithValue("booking_id", booking.Id);
                await using var reader = await lockLegs.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken)) legAllocations.Add(new LegAllocation(reader.GetGuid(0), reader.GetInt32(1)));
            }
            if (fareAllocations.Count == 0 || legAllocations.Count == 0)
                return await RollbackConflictAsync(transaction, "inventory_record_missing", "Koltuk kaydı bulunamadığı için rezervasyon güvenli biçimde iptal edilemedi.", cancellationToken);

            foreach (var allocation in fareAllocations)
            {
                await using var restoreFare = new NpgsqlCommand("UPDATE flight_fares SET seats_available=seats_available+@seats WHERE id=@fare_id", connection);
                restoreFare.Parameters.AddWithValue("fare_id", allocation.FareId);
                restoreFare.Parameters.AddWithValue("seats", allocation.Seats);
                if (await restoreFare.ExecuteNonQueryAsync(cancellationToken) != 1)
                    return await RollbackConflictAsync(transaction, "inventory_record_missing", "Bilet sınıfı bulunamadığı için iptal tamamlanamadı.", cancellationToken);
            }
            foreach (var allocation in legAllocations)
            {
                await using var restoreLegs = new NpgsqlCommand("UPDATE flight_legs SET seats_available=seats_available+@seats WHERE id=@leg_id", connection);
                restoreLegs.Parameters.AddWithValue("leg_id", allocation.LegId);
                restoreLegs.Parameters.AddWithValue("seats", allocation.Seats);
                if (await restoreLegs.ExecuteNonQueryAsync(cancellationToken) != 1)
                    return await RollbackConflictAsync(transaction, "inventory_record_missing", "Uçuş parçaları bulunamadığı için iptal tamamlanamadı.", cancellationToken);
            }
        }
        else
        {
            return await RollbackConflictAsync(transaction, "cancellation_not_allowed", "Rezervasyon türü iptal işlemini desteklemiyor.", cancellationToken);
        }

        await using (var update = new NpgsqlCommand("UPDATE app_bookings SET status='cancelled', cancelled_at=now() WHERE id=@id AND user_id=@user_id AND status='simulated'", connection))
        {
            update.Parameters.AddWithValue("id", booking.Id);
            update.Parameters.AddWithValue("user_id", user.Id);
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
                return await RollbackConflictAsync(transaction, "cancellation_conflict", "Rezervasyon durumu değişti; iptal uygulanmadı.", cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(new { booking.Id, status = "cancelled", message = "Rezervasyon iptal edildi ve ayrılan stok güvenli biçimde geri yüklendi." });
    }

    private static async Task<BookingRecord?> FindOwnedAsync(NpgsqlConnection connection, Guid userId, Guid id, bool forUpdate, CancellationToken cancellationToken)
    {
        var lockClause = forUpdate ? " FOR UPDATE" : "";
        await using var command = new NpgsqlCommand(
            "SELECT id, reference_code, kind, title, status, total_price, BTRIM(currency), details::text, created_at, confirmed_at, cancelled_at FROM app_bookings WHERE id=@id AND user_id=@user_id" + lockClause,
            connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("user_id", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRecord(reader) : null;
    }

    private static BookingRecord ReadRecord(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.IsDBNull(1) ? null : reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetDecimal(5),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.IsDBNull(7) ? null : reader.GetString(7),
        reader.GetDateTime(8),
        reader.IsDBNull(9) ? null : reader.GetDateTime(9),
        reader.IsDBNull(10) ? null : reader.GetDateTime(10));

    private static object ToListItem(BookingRecord booking, DateOnly today)
    {
        var travel = ReadTravel(booking);
        var cancellation = GetCancellation(booking, travel, today);
        return new
        {
            booking.Id,
            referenceCode = booking.ReferenceCode ?? "Numara oluşturulmamış",
            booking.Kind,
            booking.Title,
            booking.Status,
            booking.TotalPrice,
            booking.Currency,
            travel.StartDate,
            travel.EndDate,
            booking.CreatedAt,
            booking.ConfirmedAt,
            booking.CancelledAt,
            cancellation.CanCancel,
            cancellation.Reason
        };
    }

    private static object ToDetail(BookingRecord booking, DateOnly today)
    {
        var travel = ReadTravel(booking);
        var people = ReadPeople(booking.Details);
        var cancellation = GetCancellation(booking, travel, today);
        return new
        {
            booking.Id,
            referenceCode = booking.ReferenceCode ?? "Numara oluşturulmamış",
            booking.Kind,
            booking.Title,
            booking.Status,
            booking.TotalPrice,
            booking.Currency,
            booking.CreatedAt,
            booking.ConfirmedAt,
            booking.CancelledAt,
            travel,
            people.Travelers,
            people.Contact,
            cancellation.CanCancel,
            cancellation.Reason
        };
    }

    private static BookingTravel ReadTravel(BookingRecord booking)
    {
        if (!TryDocument(booking.Details, out var document)) return new(null, null, 0, 0, 0, null, null, null, null, Array.Empty<JourneyPart>());
        using (document)
        {
            var root = document.RootElement;
            if (!TryGet(root, "selection", out var selection)) return new(null, null, 0, 0, 0, null, null, null, null, Array.Empty<JourneyPart>());
            var start = ReadDate(selection, booking.Kind == "hotel" ? "CheckIn" : "DepartureDate");
            var end = ReadDate(selection, booking.Kind == "hotel" ? "CheckOut" : "ReturnDate");
            var journeys = new List<JourneyPart>();
            if (TryGet(root, "journey", out var journey))
            {
                AddJourney(journeys, journey, "outbound", "Gidiş");
                AddJourney(journeys, journey, "inbound", "Dönüş");
            }
            return new(
                start,
                end,
                ReadInt(selection, "Adults"),
                ReadInt(selection, "Children"),
                ReadInt(selection, "Infants"),
                booking.Kind == "hotel" ? ReadInt(selection, "Rooms") : null,
                ReadString(selection, "From"),
                ReadString(selection, "To"),
                ReadString(selection, "TripType"),
                journeys);
        }
    }

    private static BookingPeople ReadPeople(string? json)
    {
        if (!TryDocument(json, out var document)) return new(Array.Empty<Traveler>(), null);
        using (document)
        {
            if (!TryGet(document.RootElement, "people", out var people)) return new(Array.Empty<Traveler>(), null);
            var travelers = new List<Traveler>();
            if (TryGet(people, "Travelers", out var travelerArray) && travelerArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in travelerArray.EnumerateArray()) travelers.Add(new(
                    ReadString(item, "Type") ?? "unknown",
                    ReadString(item, "FirstName") ?? "",
                    ReadString(item, "LastName") ?? "",
                    ReadNullableInt(item, "Age"),
                    ReadNullableInt(item, "AccompanyingAdultIndex")));
            }
            Contact? contact = null;
            if (TryGet(people, "Contact", out var contactElement)) contact = new(
                ReadString(contactElement, "Name") ?? "",
                ReadString(contactElement, "Email") ?? "",
                ReadString(contactElement, "Phone") ?? "");
            return new(travelers, contact);
        }
    }

    private static void AddJourney(List<JourneyPart> result, JsonElement journey, string property, string label)
    {
        if (!TryGet(journey, property, out var item) || item.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return;
        JsonElement fare = default;
        var hasFare = TryGet(item, "Fare", out fare);
        result.Add(new(
            label,
            ReadString(item, "Airline"),
            ReadString(item, "FlightNumber"),
            ReadDateTime(item, "DepartureAt"),
            ReadDateTime(item, "ArrivalAt"),
            ReadInt(item, "Stops"),
            hasFare ? ReadString(fare, "Name") : null,
            hasFare ? ReadString(fare, "Baggage") : null));
    }

    private static CancellationState GetCancellation(BookingRecord booking, BookingTravel travel, DateOnly today)
    {
        if (booking.Status == "cancelled") return new(false, "Rezervasyon zaten iptal edilmiş.");
        if (booking.Status != "simulated") return new(false, "Rezervasyonun mevcut durumu iptale uygun değil.");
        if (travel.StartDate is null) return new(false, "Eski rezervasyon kaydında iptal için gerekli seyahat bilgisi bulunmuyor.");
        if (travel.StartDate <= today) return new(false, "Başlamış veya geçmiş tarihli rezervasyonlar iptal edilemez.");
        return new(true, null);
    }

    private static bool TryDocument(string? json, out JsonDocument document)
    {
        try
        {
            document = JsonDocument.Parse(json ?? "null");
            if (document.RootElement.ValueKind == JsonValueKind.Object) return true;
            document.Dispose();
        }
        catch (JsonException) { }
        document = null!;
        return false;
    }

    private static bool TryGet(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty(name, out value)) return true;
            foreach (var property in element.EnumerateObject())
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) { value = property.Value; return true; }
        }
        value = default;
        return false;
    }

    private static string? ReadString(JsonElement element, string name) => TryGet(element, name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static int ReadInt(JsonElement element, string name) => TryGet(element, name, out var value) && value.TryGetInt32(out var number) ? number : 0;
    private static int? ReadNullableInt(JsonElement element, string name) => TryGet(element, name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;
    private static DateOnly? ReadDate(JsonElement element, string name) => DateOnly.TryParse(ReadString(element, name), out var value) ? value : null;
    private static DateTime? ReadDateTime(JsonElement element, string name) => DateTime.TryParse(ReadString(element, name), out var value) ? value : null;

    private static async Task<IResult> RollbackConflictAsync(NpgsqlTransaction transaction, string code, string message, CancellationToken cancellationToken)
    {
        await transaction.RollbackAsync(cancellationToken);
        return Results.Conflict(new { code, message });
    }

    private sealed record BookingRecord(Guid Id, string? ReferenceCode, string Kind, string Title, string Status, decimal? TotalPrice, string? Currency, string? Details, DateTime CreatedAt, DateTime? ConfirmedAt, DateTime? CancelledAt);
    private sealed record BookingTravel(DateOnly? StartDate, DateOnly? EndDate, int Adults, int Children, int Infants, int? Rooms, string? Origin, string? Destination, string? TripType, IReadOnlyList<JourneyPart> Journeys);
    private sealed record JourneyPart(string Label, string? Airline, string? FlightNumber, DateTime? DepartureAt, DateTime? ArrivalAt, int Stops, string? FareName, string? Baggage);
    private sealed record Traveler(string Type, string FirstName, string LastName, int? Age, int? AccompanyingAdultIndex);
    private sealed record Contact(string Name, string Email, string Phone);
    private sealed record BookingPeople(IReadOnlyList<Traveler> Travelers, Contact? Contact);
    private sealed record CancellationState(bool CanCancel, string? Reason);
    private sealed record FareAllocation(Guid FareId, int Seats);
    private sealed record LegAllocation(Guid LegId, int Seats);
}
