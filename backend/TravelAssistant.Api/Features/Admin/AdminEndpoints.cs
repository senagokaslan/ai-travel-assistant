using Npgsql;
using TravelAssistant.Api.Features.Auth;

namespace TravelAssistant.Api.Features.Admin;

internal static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/summary", (HttpRequest request, SessionStore sessions) => sessions.TryGet(request, out var session) && session.Role == "admin" ? Results.Ok(new { message = "Yönetim erişimi doğrulandı." }) : Results.Forbid());
        app.MapPost("/api/admin/hotels", AddHotelAsync);
        app.MapPatch("/api/admin/hotels/{id:guid}/status", UpdateHotelStatusAsync);
        app.MapPatch("/api/admin/fares/{id:guid}", UpdateFareAsync);
        return app;
    }

    private static async Task<IResult> AddHotelAsync(AdminHotelRequest request, HttpRequest httpRequest, IConfiguration configuration, SessionStore sessions, CancellationToken cancellationToken)
    {
        if (!sessions.TryGet(httpRequest, out var session) || session.Role != "admin") return Results.Forbid();
        if (string.IsNullOrWhiteSpace(request.Name) || request.Stars is < 1 or > 5 || request.Rating is < 0 or > 5) return Results.BadRequest(new { message = "Otel adı, yıldız (1-5) ve puan (0-5) geçerli olmalı." });
        await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Postgres")); await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("INSERT INTO hotels (name, city_id, district, stars, rating, description) VALUES (@name,@city,@district,@stars,@rating,@description) RETURNING id", connection);
        command.Parameters.AddWithValue("name", request.Name.Trim()); command.Parameters.AddWithValue("city", request.CityId); command.Parameters.AddWithValue("district", request.District.Trim()); command.Parameters.AddWithValue("stars", request.Stars); command.Parameters.AddWithValue("rating", request.Rating); command.Parameters.AddWithValue("description", request.Description?.Trim() ?? "");
        var id = await command.ExecuteScalarAsync(cancellationToken); return Results.Created($"/api/hotels/{id}", new { id, message = "Otel kataloğa eklendi." });
    }

    private static async Task<IResult> UpdateHotelStatusAsync(Guid id, StatusRequest request, HttpRequest httpRequest, IConfiguration configuration, SessionStore sessions, CancellationToken cancellationToken)
    {
        if (!sessions.TryGet(httpRequest, out var session) || session.Role != "admin") return Results.Forbid();
        await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Postgres")); await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("UPDATE hotels SET is_active=@active WHERE id=@id", connection); command.Parameters.AddWithValue("id", id); command.Parameters.AddWithValue("active", request.IsActive);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 0 ? Results.NotFound() : Results.Ok(new { message = request.IsActive ? "Otel satışa açıldı." : "Otel satışa kapatıldı." });
    }

    private static async Task<IResult> UpdateFareAsync(Guid id, AdminFareRequest request, HttpRequest httpRequest, IConfiguration configuration, SessionStore sessions, CancellationToken cancellationToken)
    {
        if (!sessions.TryGet(httpRequest, out var session) || session.Role != "admin") return Results.Forbid();
        if (request.Price <= 0 || request.SeatsAvailable < 0) return Results.BadRequest(new { message = "Fiyat sıfırdan büyük, koltuk sayısı negatif olamaz." });
        await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Postgres")); await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("UPDATE flight_fares SET price=@price, seats_available=@seats, is_active=@active WHERE id=@id", connection);
        command.Parameters.AddWithValue("id", id); command.Parameters.AddWithValue("price", request.Price); command.Parameters.AddWithValue("seats", request.SeatsAvailable); command.Parameters.AddWithValue("active", request.IsActive);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 0 ? Results.NotFound() : Results.Ok(new { message = "Bilet seçeneği güncellendi." });
    }
}
