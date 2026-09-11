using Npgsql;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile(
    "appsettings.Local.json",
    optional: true,
    reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod());
});

var app = builder.Build();
var sessions = new ConcurrentDictionary<string, SessionUser>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

app.MapPost("/api/auth/register", async (RegisterRequest request, IConfiguration configuration, CancellationToken cancellationToken) =>
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
        command.Parameters.AddWithValue("name", request.Name.Trim());
        command.Parameters.AddWithValue("email", email);
        command.Parameters.AddWithValue("hash", HashPassword(request.Password));
        var id = (Guid)(await command.ExecuteScalarAsync(cancellationToken))!;
        return Results.Ok(new { message = "Hesabınız oluşturuldu.", user = new { id, name = request.Name.Trim(), email, role = "user" } });
    }
    catch (PostgresException exception) when (exception.SqlState == "23505")
    {
        return Results.Conflict(new { message = "Bu e-posta adresi zaten kayıtlı. Giriş yapmayı deneyin." });
    }
    catch (NpgsqlException) { return Results.Problem("Kayıt sırasında veritabanına bağlanılamadı.", statusCode: 503); }
});

app.MapPost("/api/auth/login", async (LoginRequest request, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    var email = request.Email.Trim().ToLowerInvariant();
    if (!Regex.IsMatch(email, @"^[^\s@]+@[^\s@]+\.[^\s@]+$")) return Results.BadRequest(new { message = "Geçerli bir e-posta adresi yazın." });
    if (string.IsNullOrEmpty(request.Password)) return Results.BadRequest(new { message = "Parolanızı yazın." });
    var connectionString = configuration.GetConnectionString("Postgres");
    if (string.IsNullOrWhiteSpace(connectionString)) return Results.Problem("Giriş için PostgreSQL bağlantısı yapılandırılmamış.", statusCode: 503);
    try
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT id, name, email, password_hash, role FROM app_users WHERE email = @email", connection);
        command.Parameters.AddWithValue("email", email);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || !VerifyPassword(request.Password, reader.GetString(3))) return Results.Unauthorized();
        var user = new SessionUser(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(4));
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        sessions[token] = user;
        return Results.Ok(new { token, user });
    }
    catch (NpgsqlException) { return Results.Problem("Giriş sırasında veritabanına bağlanılamadı.", statusCode: 503); }
});

app.MapGet("/api/auth/me", (HttpRequest request) =>
    TryGetSession(request, sessions, out var user) ? Results.Ok(new { user }) : Results.Unauthorized());

app.MapPost("/api/auth/logout", (HttpRequest request) =>
{
    var token = request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase).Trim();
    if (!string.IsNullOrWhiteSpace(token)) sessions.TryRemove(token, out _);
    return Results.NoContent();
});

app.MapGet("/api/profile", async (HttpRequest request, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    if (!TryGetSession(request, sessions, out var session)) return Results.Unauthorized();
    await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Postgres"));
    await connection.OpenAsync(cancellationToken);
    await using var command = new NpgsqlCommand("SELECT id, name, email, role, COALESCE(phone, ''), currency FROM app_users WHERE id = @id", connection);
    command.Parameters.AddWithValue("id", session.Id);
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    return await reader.ReadAsync(cancellationToken) ? Results.Ok(new { id = reader.GetGuid(0), name = reader.GetString(1), email = reader.GetString(2), role = reader.GetString(3), phone = reader.GetString(4), currency = reader.GetString(5) }) : Results.NotFound(new { message = "Kullanıcı hesabı bulunamadı." });
});

app.MapGet("/api/bookings", async (HttpRequest request, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    if (!TryGetSession(request, sessions, out var session)) return Results.Unauthorized();
    await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Postgres")); await connection.OpenAsync(cancellationToken);
    await using var command = new NpgsqlCommand("SELECT id, kind, title, status, created_at FROM app_bookings WHERE user_id = @user_id ORDER BY created_at DESC", connection); command.Parameters.AddWithValue("user_id", session.Id);
    await using var reader = await command.ExecuteReaderAsync(cancellationToken); var bookings = new List<object>();
    while (await reader.ReadAsync(cancellationToken)) bookings.Add(new { id = reader.GetGuid(0), kind = reader.GetString(1), title = reader.GetString(2), status = reader.GetString(3), createdAt = reader.GetDateTime(4) });
    return Results.Ok(bookings);
});

app.MapPatch("/api/profile", async (HttpRequest request, ProfileUpdate update, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    if (!TryGetSession(request, sessions, out var session)) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(update.Name) || update.Name.Trim().Length < 2) return Results.BadRequest(new { message = "Ad soyad en az 2 karakter olmalı." });
    if (!Regex.IsMatch(update.Currency ?? "", "^[A-Z]{3}$")) return Results.BadRequest(new { message = "Para birimi geçersiz." });
    await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Postgres"));
    await connection.OpenAsync(cancellationToken);
    await using var command = new NpgsqlCommand("UPDATE app_users SET name = @name, phone = @phone, currency = @currency WHERE id = @id RETURNING id, name, email, role, COALESCE(phone, ''), currency", connection);
    command.Parameters.AddWithValue("id", session.Id); command.Parameters.AddWithValue("name", update.Name.Trim()); command.Parameters.AddWithValue("phone", (object?)update.Phone?.Trim() ?? DBNull.Value); command.Parameters.AddWithValue("currency", update.Currency!);
    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
    if (!await reader.ReadAsync(cancellationToken)) return Results.NotFound(new { message = "Kullanıcı hesabı bulunamadı." });
    var updated = new SessionUser(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)); sessions[request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase).Trim()] = updated;
    return Results.Ok(new { message = "Profiliniz güncellendi.", id = reader.GetGuid(0), name = reader.GetString(1), email = reader.GetString(2), role = reader.GetString(3), phone = reader.GetString(4), currency = reader.GetString(5) });
});

app.MapGet("/api/admin/summary", (HttpRequest request) => TryGetSession(request, sessions, out var session) && session.Role == "admin" ? Results.Ok(new { message = "Yönetim erişimi doğrulandı." }) : Results.Forbid());

app.MapGet("/api", () => Results.Ok(new
{
    name = "Bağımsız AI Destekli Seyahat Asistanı API",
    description = "Otel ve uçuş arama simülasyonu için başlangıç API'si.",
    documentation = "/swagger",
    health = "/api/health"
}));

app.MapGet("/api/travel/airports", async (string? q, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    var term = (q ?? "").Trim();
    await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Postgres")); await connection.OpenAsync(cancellationToken);
    await using var command = new NpgsqlCommand("SELECT a.iata_code, a.name, c.name, co.name FROM airports a JOIN travel_cities c ON c.id = a.city_id JOIN travel_countries co ON co.id = c.country_id WHERE a.is_active AND (@q = '' OR a.iata_code ILIKE @like OR a.name ILIKE @like OR c.name ILIKE @like) ORDER BY c.name, a.name", connection);
    command.Parameters.AddWithValue("q", term); command.Parameters.AddWithValue("like", $"%{term}%");
    await using var reader = await command.ExecuteReaderAsync(cancellationToken); var results = new List<object>(); while (await reader.ReadAsync(cancellationToken)) results.Add(new { code = reader.GetString(0).Trim(), name = reader.GetString(1), city = reader.GetString(2), country = reader.GetString(3) });
    return Results.Ok(results);
});

app.MapGet("/api/hotels", async (string? q, DateOnly? checkIn, DateOnly? checkOut, int? adults, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    if (checkIn is not null && checkIn < DateOnly.FromDateTime(DateTime.UtcNow)) return Results.BadRequest(new { message = "Geçmiş tarih için arama yapılamaz." });
    if (checkIn is not null && (checkOut is null || checkOut <= checkIn)) return Results.BadRequest(new { message = "Çıkış tarihi giriş tarihinden sonra olmalı." });
    if (adults is not null && (adults < 1 || adults > 20)) return Results.BadRequest(new { message = "Kişi sayısı 1-20 arasında olmalı." });
    var nights = checkIn is not null && checkOut is not null ? checkOut.Value.DayNumber - checkIn.Value.DayNumber : 0;
    await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Postgres")); await connection.OpenAsync(cancellationToken);
    await using var command = new NpgsqlCommand("SELECT h.id, h.name, c.name, h.district, h.stars, h.rating, h.description, COALESCE(array_agg(DISTINCT r.name) FILTER (WHERE r.is_active), '{}'), COALESCE(SUM(rr.nightly_price), 0) FROM hotels h JOIN travel_cities c ON c.id = h.city_id LEFT JOIN hotel_rooms r ON r.hotel_id = h.id LEFT JOIN room_daily_rates rr ON rr.room_id = r.id AND (@check_in IS NULL OR rr.stay_date >= @check_in AND rr.stay_date < @check_out) WHERE h.is_active AND (@q = '' OR h.name ILIKE @like OR c.name ILIKE @like OR h.district ILIKE @like) AND (@check_in IS NULL OR EXISTS (SELECT 1 FROM hotel_rooms ar JOIN room_daily_rates arrr ON arrr.room_id = ar.id WHERE ar.hotel_id = h.id AND ar.is_active AND ar.capacity >= COALESCE(@adults, 1) GROUP BY ar.id HAVING COUNT(*) = @nights AND MIN(arrr.rooms_available) > 0)) GROUP BY h.id, c.name ORDER BY h.rating DESC", connection); command.Parameters.AddWithValue("q", (q ?? "").Trim()); command.Parameters.AddWithValue("like", $"%{(q ?? "").Trim()}%"); command.Parameters.AddWithValue("check_in", (object?)checkIn?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value); command.Parameters.AddWithValue("check_out", (object?)checkOut?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value); command.Parameters.AddWithValue("adults", (object?)adults ?? DBNull.Value); command.Parameters.AddWithValue("nights", nights);
    await using var reader = await command.ExecuteReaderAsync(cancellationToken); var hotels = new List<object>(); while (await reader.ReadAsync(cancellationToken)) hotels.Add(new { id = reader.GetGuid(0), name = reader.GetString(1), city = reader.GetString(2), district = reader.GetString(3), stars = reader.GetInt16(4), rating = reader.GetDecimal(5), description = reader.GetString(6), rooms = reader.GetFieldValue<string[]>(7), totalPrice = reader.GetDecimal(8) }); return Results.Ok(hotels);
});

app.MapGet("/api/hotels/{id:guid}", async (Guid id, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Postgres")); await connection.OpenAsync(cancellationToken); await using var command = new NpgsqlCommand("SELECT h.id, h.name, c.name, h.district, h.stars, h.rating, h.description, r.id, r.name, r.capacity, r.features FROM hotels h JOIN travel_cities c ON c.id = h.city_id LEFT JOIN hotel_rooms r ON r.hotel_id = h.id AND r.is_active WHERE h.id = @id AND h.is_active ORDER BY r.capacity", connection); command.Parameters.AddWithValue("id", id); await using var reader = await command.ExecuteReaderAsync(cancellationToken); if (!await reader.ReadAsync(cancellationToken)) return Results.NotFound(new { message = "Otel bulunamadı veya satışa kapalı." }); var rooms = new List<object>(); var hotel = new { id = reader.GetGuid(0), name = reader.GetString(1), city = reader.GetString(2), district = reader.GetString(3), stars = reader.GetInt16(4), rating = reader.GetDecimal(5), description = reader.GetString(6) }; do { if (!reader.IsDBNull(7)) rooms.Add(new { id = reader.GetGuid(7), name = reader.GetString(8), capacity = reader.GetInt16(9), features = reader.GetFieldValue<string[]>(10) }); } while (await reader.ReadAsync(cancellationToken)); return Results.Ok(new { hotel, rooms });
});

app.MapGet("/api/health", async (
    IConfiguration configuration,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var checkedAt = DateTimeOffset.UtcNow;
    var connectionString = configuration.GetConnectionString("Postgres");

    if (string.IsNullOrWhiteSpace(connectionString))
    {
        return Results.Json(new
        {
            status = "unhealthy",
            checkedAt,
            api = new { status = "healthy" },
            database = new
            {
                status = "unhealthy",
                message = "PostgreSQL bağlantı ayarı bulunamadı. appsettings.Local.json dosyasını oluşturun veya ConnectionStrings__Postgres ortam değişkenini tanımlayın."
            }
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    try
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            "SELECT current_database(), current_user",
            connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        return Results.Ok(new
        {
            status = "healthy",
            checkedAt,
            api = new { status = "healthy" },
            database = new
            {
                status = "healthy",
                name = reader.GetString(0),
                user = reader.GetString(1)
            }
        });
    }
    catch (Exception exception) when (
        exception is NpgsqlException or TimeoutException or InvalidOperationException)
    {
        logger.LogWarning(exception, "PostgreSQL sağlık kontrolü başarısız oldu.");

        return Results.Json(new
        {
            status = "unhealthy",
            checkedAt,
            api = new { status = "healthy" },
            database = new
            {
                status = "unhealthy",
                message = "PostgreSQL'e bağlanılamadı. Veritabanının çalıştığını ve yerel bağlantı ayarlarını kontrol edin."
            }
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
})
.WithName("GetHealth")
.WithOpenApi();

app.Run();

static string? ValidateCredentials(string name, string email, string password)
{
    if (string.IsNullOrWhiteSpace(name) || name.Trim().Length < 2) return "Ad soyad en az 2 karakter olmalı.";
    if (!Regex.IsMatch(email.Trim(), @"^[^\s@]+@[^\s@]+\.[^\s@]+$")) return "Geçerli bir e-posta adresi yazın.";
    if (password.Length < 8 || !password.Any(char.IsUpper) || !password.Any(char.IsLower) || !password.Any(char.IsDigit)) return "Parola en az 8 karakter olmalı; büyük harf, küçük harf ve rakam içermeli.";
    return null;
}

static string HashPassword(string password)
{
    var salt = RandomNumberGenerator.GetBytes(16);
    var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 120_000, HashAlgorithmName.SHA256, 32);
    return $"pbkdf2-sha256$120000${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
}

static bool VerifyPassword(string password, string stored)
{
    var parts = stored.Split('$');
    if (parts.Length != 4 || !int.TryParse(parts[1], out var iterations)) return false;
    var expected = Convert.FromBase64String(parts[3]);
    var actual = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(parts[2]), iterations, HashAlgorithmName.SHA256, expected.Length);
    return CryptographicOperations.FixedTimeEquals(actual, expected);
}

static bool TryGetSession(HttpRequest request, ConcurrentDictionary<string, SessionUser> sessions, out SessionUser user)
{
    var token = request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase).Trim();
    return sessions.TryGetValue(token, out user!);
}

record RegisterRequest(string Name, string Email, string Password);
record LoginRequest(string Email, string Password);
record SessionUser(Guid Id, string Name, string Email, string Role);
record ProfileUpdate(string Name, string? Phone, string? Currency);
