using Npgsql;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using TravelAssistant.Api.Infrastructure;

namespace TravelAssistant.Api.Features.Auth;

internal static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/register", RegisterAsync);
        app.MapPost("/api/auth/login", LoginAsync);
        app.MapGet("/api/auth/me", async (HttpRequest request, IConfiguration configuration, SessionStore sessions, CancellationToken cancellationToken) =>
        {
            var user = await sessions.GetValidUserAsync(request, configuration, cancellationToken);
            return user is null ? Results.Unauthorized() : Results.Ok(new { user });
        });
        app.MapPost("/api/auth/logout", (HttpRequest request, SessionStore sessions) => { sessions.Remove(request); return Results.NoContent(); });
        return app;
    }

    private static async Task<IResult> RegisterAsync(RegisterRequest request, HttpContext context, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var validation = ValidateCredentials(request.Name, request.Email, request.Password);
        if (validation is not null) return ApiErrorResults.Create(context, 400, "invalid_account_details", validation, "Bilgileri düzeltip tekrar deneyin.");
        var email = request.Email.Trim().ToLowerInvariant();
        var connectionString = configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(connectionString)) throw new SafeApiException(ApiErrorCatalog.DatabaseUnavailable);
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("INSERT INTO app_users (name, email, password_hash) VALUES (@name, @email, @hash) RETURNING id", connection);
            command.Parameters.AddWithValue("name", request.Name.Trim()); command.Parameters.AddWithValue("email", email); command.Parameters.AddWithValue("hash", HashPassword(request.Password));
            var id = (Guid)(await command.ExecuteScalarAsync(cancellationToken))!;
            return Results.Ok(new { message = "Hesabınız oluşturuldu.", user = new { id, name = request.Name.Trim(), email, role = "user" } });
        }
        catch (PostgresException exception) when (exception.SqlState == "23505") { return ApiErrorResults.Create(context, 409, "email_already_registered", "Bu e-posta adresi zaten kayıtlı.", "Yeni hesap açmak yerine giriş yapmayı deneyin."); }
    }

    private static async Task<IResult> LoginAsync(LoginRequest request, HttpContext context, IConfiguration configuration, SessionStore sessions, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (!Regex.IsMatch(email, @"^[^\s@]+@[^\s@]+\.[^\s@]+$")) return ApiErrorResults.Create(context, 400, "invalid_account_details", "Geçerli bir e-posta adresi yazın.", "E-posta biçimini kontrol edip tekrar deneyin.");
        if (string.IsNullOrEmpty(request.Password) || request.Password.Length > 128) return ApiErrorResults.Create(context, 400, "invalid_account_details", "Parola geçerli değil.", "Parola alanını kontrol edip tekrar deneyin.");
        var connectionString = configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(connectionString)) throw new SafeApiException(ApiErrorCatalog.DatabaseUnavailable);
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT id, name, email, password_hash, role FROM app_users WHERE email = @email", connection); command.Parameters.AddWithValue("email", email);
        Guid id;
        string name, storedEmail, storedHash, role;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken)) return Results.Unauthorized();
            id = reader.GetGuid(0); name = reader.GetString(1); storedEmail = reader.GetString(2); storedHash = reader.GetString(3); role = reader.GetString(4);
        }
        if (!VerifyPassword(request.Password, storedHash)) return Results.Unauthorized();
        if (NeedsRehash(storedHash))
        {
            await using var upgrade = new NpgsqlCommand("UPDATE app_users SET password_hash = @hash WHERE id = @id", connection);
            upgrade.Parameters.AddWithValue("hash", HashPassword(request.Password)); upgrade.Parameters.AddWithValue("id", id);
            await upgrade.ExecuteNonQueryAsync(cancellationToken);
        }
        var user = new SessionUser(id, name, storedEmail, role);
        return Results.Ok(new { token = sessions.Create(user), user });
    }

    private static string? ValidateCredentials(string name, string email, string password)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length < 2) return "Ad soyad en az 2 karakter olmalı.";
        if (!Regex.IsMatch(email.Trim(), @"^[^\s@]+@[^\s@]+\.[^\s@]+$")) return "Geçerli bir e-posta adresi yazın.";
        if (password.Length is < 8 or > 128 || !password.Any(char.IsUpper) || !password.Any(char.IsLower) || !password.Any(char.IsDigit)) return "Parola 8-128 karakter olmalı; büyük harf, küçük harf ve rakam içermeli.";
        return null;
    }

    private static string HashPassword(string password)
    {
        const int iterations = 600_000;
        var salt = RandomNumberGenerator.GetBytes(16); var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2-sha256${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    private static bool VerifyPassword(string password, string stored)
    {
        try
        {
            var parts = stored.Split('$');
            if (parts.Length != 4 || parts[0] != "pbkdf2-sha256" || !int.TryParse(parts[1], out var iterations) || iterations is < 100_000 or > 1_000_000) return false;
            var expected = Convert.FromBase64String(parts[3]);
            if (expected.Length != 32) return false;
            var salt = Convert.FromBase64String(parts[2]);
            if (salt.Length < 16) return false;
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }

    private static bool NeedsRehash(string stored) => stored.Split('$') is { Length: 4 } parts && int.TryParse(parts[1], out var iterations) && iterations < 600_000;
}
