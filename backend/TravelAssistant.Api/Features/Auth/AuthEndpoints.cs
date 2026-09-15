using Npgsql;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace TravelAssistant.Api.Features.Auth;

internal static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/register", RegisterAsync);
        app.MapPost("/api/auth/login", LoginAsync);
        app.MapGet("/api/auth/me", (HttpRequest request, SessionStore sessions) => sessions.TryGet(request, out var user) ? Results.Ok(new { user }) : Results.Unauthorized());
        app.MapPost("/api/auth/logout", (HttpRequest request, SessionStore sessions) => { sessions.Remove(request); return Results.NoContent(); });
        return app;
    }

    private static async Task<IResult> RegisterAsync(RegisterRequest request, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var validation = ValidateCredentials(request.Name, request.Email, request.Password);
        if (validation is not null) return Results.BadRequest(new { message = validation });
        var email = request.Email.Trim().ToLowerInvariant();
        var connectionString = configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(connectionString)) return Results.Problem("Kayıt için PostgreSQL bağlantısı yapılandırılmamış.", statusCode: 503);
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("INSERT INTO app_users (name, email, password_hash) VALUES (@name, @email, @hash) RETURNING id", connection);
            command.Parameters.AddWithValue("name", request.Name.Trim()); command.Parameters.AddWithValue("email", email); command.Parameters.AddWithValue("hash", HashPassword(request.Password));
            var id = (Guid)(await command.ExecuteScalarAsync(cancellationToken))!;
            return Results.Ok(new { message = "Hesabınız oluşturuldu.", user = new { id, name = request.Name.Trim(), email, role = "user" } });
        }
        catch (PostgresException exception) when (exception.SqlState == "23505") { return Results.Conflict(new { message = "Bu e-posta adresi zaten kayıtlı. Giriş yapmayı deneyin." }); }
        catch (NpgsqlException) { return Results.Problem("Kayıt sırasında veritabanına bağlanılamadı.", statusCode: 503); }
    }

    private static async Task<IResult> LoginAsync(LoginRequest request, IConfiguration configuration, SessionStore sessions, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (!Regex.IsMatch(email, @"^[^\s@]+@[^\s@]+\.[^\s@]+$")) return Results.BadRequest(new { message = "Geçerli bir e-posta adresi yazın." });
        if (string.IsNullOrEmpty(request.Password)) return Results.BadRequest(new { message = "Parolanızı yazın." });
        var connectionString = configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(connectionString)) return Results.Problem("Giriş için PostgreSQL bağlantısı yapılandırılmamış.", statusCode: 503);
        try
        {
            await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT id, name, email, password_hash, role FROM app_users WHERE email = @email", connection); command.Parameters.AddWithValue("email", email);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken) || !VerifyPassword(request.Password, reader.GetString(3))) return Results.Unauthorized();
            var user = new SessionUser(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(4));
            return Results.Ok(new { token = sessions.Create(user), user });
        }
        catch (NpgsqlException) { return Results.Problem("Giriş sırasında veritabanına bağlanılamadı.", statusCode: 503); }
    }

    private static string? ValidateCredentials(string name, string email, string password)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length < 2) return "Ad soyad en az 2 karakter olmalı.";
        if (!Regex.IsMatch(email.Trim(), @"^[^\s@]+@[^\s@]+\.[^\s@]+$")) return "Geçerli bir e-posta adresi yazın.";
        if (password.Length < 8 || !password.Any(char.IsUpper) || !password.Any(char.IsLower) || !password.Any(char.IsDigit)) return "Parola en az 8 karakter olmalı; büyük harf, küçük harf ve rakam içermeli.";
        return null;
    }

    private static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16); var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 120_000, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2-sha256$120000${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    private static bool VerifyPassword(string password, string stored)
    {
        var parts = stored.Split('$'); if (parts.Length != 4 || !int.TryParse(parts[1], out var iterations)) return false;
        var expected = Convert.FromBase64String(parts[3]); var actual = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(parts[2]), iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
