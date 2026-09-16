using Npgsql;
using TravelAssistant.Api.Infrastructure;

namespace TravelAssistant.Api.Features.Hotels;

internal static class HotelEndpoints
{
    public static IEndpointRouteBuilder MapHotelEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/travel/cities", GetCitiesAsync);
        app.MapGet("/api/hotels/locations", SearchLocationsAsync);
        app.MapGet("/api/hotels", SearchHotelsAsync);
        app.MapGet("/api/hotels/{id:guid}/availability", GetAvailabilityAsync);
        app.MapGet("/api/hotels/{id:guid}", GetHotelAsync);
        return app;
    }

    private static async Task<IResult> GetCitiesAsync(IConfiguration configuration, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Postgres")); await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT id,name FROM travel_cities ORDER BY name", connection); await using var reader = await command.ExecuteReaderAsync(cancellationToken); var list = new List<object>();
        while (await reader.ReadAsync(cancellationToken)) list.Add(new { id = reader.GetGuid(0), name = reader.GetString(1) }); return Results.Ok(list);
    }

    private static async Task<IResult> SearchLocationsAsync(string? q, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var term = (q ?? "").Trim(); if (term.Length < 2) return Results.Ok(Array.Empty<object>());
        await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Postgres")); await connection.OpenAsync(cancellationToken);
        const string sql = "SELECT 'city:' || c.id, c.name, c.name, NULL::text, 'city' FROM travel_cities c WHERE c.name ILIKE @like AND EXISTS (SELECT 1 FROM hotels h WHERE h.city_id=c.id AND h.is_active) UNION ALL SELECT 'hotel:' || h.id, h.name, c.name, h.district, 'hotel' FROM hotels h JOIN travel_cities c ON c.id=h.city_id WHERE h.is_active AND (h.name ILIKE @like OR c.name ILIKE @like OR h.district ILIKE @like) ORDER BY 5, 2 LIMIT 10";
        await using var command = new NpgsqlCommand(sql, connection); command.Parameters.AddWithValue("like", $"%{term}%"); await using var reader = await command.ExecuteReaderAsync(cancellationToken); var locations = new List<object>();
        while (await reader.ReadAsync(cancellationToken)) locations.Add(new { key = reader.GetString(0), label = reader.GetString(1), city = reader.GetString(2), district = reader.IsDBNull(3) ? null : reader.GetString(3), type = reader.GetString(4) }); return Results.Ok(locations);
    }

    private static async Task<IResult> SearchHotelsAsync(string? q, DateOnly? checkIn, DateOnly? checkOut, int? adults, int? rooms, int? children, string? childAges, HttpContext context, IConfiguration configuration, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(q)) return Invalid(context, "location_not_found", "Şehir veya otel adı zorunludur.", "Öneri listesinden bir şehir veya otel seçin.");
        if (checkIn is null || checkOut is null) return Invalid(context, "invalid_date", "Giriş ve çıkış tarihleri zorunludur.", "Her iki tarihi de seçip tekrar arayın.");
        var arrival = checkIn.Value; var departure = checkOut.Value;
        if (arrival < DateOnly.FromDateTime(DateTime.Now)) return Invalid(context, "invalid_date", "Geçmiş tarih için arama yapılamaz.", "Bugün veya daha ileri bir giriş tarihi seçin.");
        if (departure <= arrival) return Invalid(context, "invalid_date", "Çıkış tarihi giriş tarihinden sonra olmalı.", "Çıkış tarihini giriş tarihinden sonraya alın.");
        if (departure.DayNumber - arrival.DayNumber > 30) return Invalid(context, "invalid_date", "Konaklama en fazla 30 gece olabilir.", "Tarih aralığını 30 geceyi aşmayacak şekilde düzenleyin.");
        if (adults is not null && (adults < 1 || adults > 20)) return Invalid(context, "invalid_guests", "Yetişkin sayısı geçersizdir.", "Yetişkin sayısını 1–20 arasında seçin.");
        if (rooms is not null && (rooms < 1 || rooms > 8)) return Invalid(context, "invalid_guests", "Oda sayısı geçersizdir.", "Oda sayısını 1–8 arasında seçin.");
        if (children is not null && (children < 0 || children > 8)) return Invalid(context, "invalid_guests", "Çocuk sayısı geçersizdir.", "Çocuk sayısını 0–8 arasında seçin.");
        var ages = ParseAges(childAges); if (ages.Length != (children ?? 0) || ages.Any(age => age is < 0 or > 17)) return Invalid(context, "invalid_guests", "Çocuk sayısı ile çocuk yaşları eşleşmiyor.", "Her çocuk için 0–17 arasında bir yaş seçin.");
        if ((adults ?? 1) < (rooms ?? 1)) return Invalid(context, "invalid_guests", "Her oda için en az bir yetişkin olmalı.", "Yetişkin sayısını oda sayısına eşit veya daha yüksek yapın.");
        await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Postgres")); await connection.OpenAsync(cancellationToken);
        await using (var location = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM travel_cities c WHERE c.name ILIKE @like UNION ALL SELECT 1 FROM hotels h JOIN travel_cities c ON c.id=h.city_id WHERE h.is_active AND (h.name ILIKE @like OR h.district ILIKE @like OR c.name ILIKE @like))", connection))
        {
            location.Parameters.AddWithValue("like", $"%{q.Trim()}%");
            if (!(bool)(await location.ExecuteScalarAsync(cancellationToken))!)
                return ApiErrorResults.Create(context, 404, "location_not_found", "Bu adla eşleşen aktif bir şehir veya otel bulunamadı.", "Konumu öneri listesinden yeniden seçin.");
        }
        return Results.Ok(await HotelAvailabilityService.SearchAsync(connection, q, arrival, departure, rooms ?? 1, (adults ?? 1) + (children ?? 0), cancellationToken));
    }

    private static async Task<IResult> GetAvailabilityAsync(Guid id, DateOnly? checkIn, DateOnly? checkOut, int? adults, int? rooms, int? children, string? childAges, IConfiguration configuration, CancellationToken cancellationToken)
    {
        if (checkIn is null || checkOut is null) return Results.BadRequest(new { message = "Giriş ve çıkış tarihleri zorunludur." });
        var arrival = checkIn.Value; var departure = checkOut.Value;
        if (arrival < DateOnly.FromDateTime(DateTime.Now)) return Results.BadRequest(new { message = "Geçmiş tarih için arama yapılamaz." });
        if (departure <= arrival || departure.DayNumber - arrival.DayNumber > 30) return Results.BadRequest(new { message = "Tarih aralığı geçersizdir." });
        if (adults is null or < 1 or > 20 || rooms is null or < 1 or > 8 || children is null or < 0 or > 8 || adults < rooms) return Results.BadRequest(new { message = "Oda ve misafir bilgileri geçersizdir." });
        var ages = ParseAges(childAges); if (ages.Length != children || ages.Any(age => age is < 0 or > 17)) return Results.BadRequest(new { message = "Çocuk sayısı ile çocuk yaşları eşleşmelidir." });
        await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Postgres")); await connection.OpenAsync(cancellationToken);
        var hotel = await HotelAvailabilityService.GetByIdAsync(connection, id, arrival, departure, rooms.Value, adults.Value + children.Value, cancellationToken);
        return hotel is null ? Results.NotFound(new { message = "Otel kaldırılmış veya seçilen tarihlerde artık müsait değil." }) : Results.Ok(hotel);
    }

    private static async Task<IResult> GetHotelAsync(Guid id, IConfiguration configuration, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Postgres")); await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT h.id, h.name, c.name, h.district, h.stars, h.rating, h.description, r.id, r.name, r.capacity, r.features FROM hotels h JOIN travel_cities c ON c.id = h.city_id LEFT JOIN hotel_rooms r ON r.hotel_id = h.id AND r.is_active WHERE h.id = @id AND h.is_active ORDER BY r.capacity", connection); command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken); if (!await reader.ReadAsync(cancellationToken)) return Results.NotFound(new { message = "Otel bulunamadı veya satışa kapalı." });
        var rooms = new List<object>(); var hotel = new { id = reader.GetGuid(0), name = reader.GetString(1), city = reader.GetString(2), district = reader.GetString(3), stars = reader.GetInt16(4), rating = reader.GetDecimal(5), description = reader.GetString(6) };
        do { if (!reader.IsDBNull(7)) rooms.Add(new { id = reader.GetGuid(7), name = reader.GetString(8), capacity = reader.GetInt16(9), features = reader.GetFieldValue<string[]>(10) }); } while (await reader.ReadAsync(cancellationToken));
        return Results.Ok(new { hotel, rooms });
    }

    private static int[] ParseAges(string? childAges) => string.IsNullOrWhiteSpace(childAges) ? Array.Empty<int>() : childAges.Split(',').Select(value => int.TryParse(value, out var age) ? age : -1).ToArray();
    private static IResult Invalid(HttpContext context, string code, string message, string action) => ApiErrorResults.Create(context, 400, code, message, action);
}
