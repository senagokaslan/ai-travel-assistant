using Npgsql;
using System.Text.RegularExpressions;
using TravelAssistant.Api.Features.Auth;

namespace TravelAssistant.Api.Features.Profile;

internal static class ProfileEndpoints
{
    public static IEndpointRouteBuilder MapProfileEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/profile", GetProfileAsync);
        app.MapPatch("/api/profile", UpdateProfileAsync);
        return app;
    }

    private static async Task<IResult> GetProfileAsync(HttpRequest request, IConfiguration configuration, SessionStore sessions, CancellationToken cancellationToken)
    {
        if (!sessions.TryGet(request, out var session)) return Results.Unauthorized();
        await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Postgres")); await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT id, name, email, role, COALESCE(phone, ''), currency FROM app_users WHERE id = @id", connection); command.Parameters.AddWithValue("id", session.Id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Results.Ok(new { id = reader.GetGuid(0), name = reader.GetString(1), email = reader.GetString(2), role = reader.GetString(3), phone = reader.GetString(4), currency = reader.GetString(5) }) : Results.NotFound(new { message = "Kullanıcı hesabı bulunamadı." });
    }

    private static async Task<IResult> UpdateProfileAsync(HttpRequest request, ProfileUpdate update, IConfiguration configuration, SessionStore sessions, CancellationToken cancellationToken)
    {
        if (!sessions.TryGet(request, out var session)) return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(update.Name) || update.Name.Trim().Length < 2) return Results.BadRequest(new { message = "Ad soyad en az 2 karakter olmalı." });
        if (!Regex.IsMatch(update.Currency ?? "", "^[A-Z]{3}$")) return Results.BadRequest(new { message = "Para birimi geçersiz." });
        await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Postgres")); await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("UPDATE app_users SET name = @name, phone = @phone, currency = @currency WHERE id = @id RETURNING id, name, email, role, COALESCE(phone, ''), currency", connection);
        command.Parameters.AddWithValue("id", session.Id); command.Parameters.AddWithValue("name", update.Name.Trim()); command.Parameters.AddWithValue("phone", (object?)update.Phone?.Trim() ?? DBNull.Value); command.Parameters.AddWithValue("currency", update.Currency!);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return Results.NotFound(new { message = "Kullanıcı hesabı bulunamadı." });
        sessions.Update(request, new SessionUser(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        return Results.Ok(new { message = "Profiliniz güncellendi.", id = reader.GetGuid(0), name = reader.GetString(1), email = reader.GetString(2), role = reader.GetString(3), phone = reader.GetString(4), currency = reader.GetString(5) });
    }

}
