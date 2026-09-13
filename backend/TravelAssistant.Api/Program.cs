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

await DatabaseMigrator.InitializeAsync(
    app.Configuration,
    app.Environment.IsDevelopment(),
    app.Logger,
    CancellationToken.None);

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

app.MapPost("/api/admin/hotels", async (AdminHotelRequest request, HttpRequest httpRequest, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    if (!TryGetSession(httpRequest, sessions, out var session) || session.Role != "admin") return Results.Forbid();
    if (string.IsNullOrWhiteSpace(request.Name) || request.Stars is < 1 or > 5 || request.Rating is < 0 or > 5) return Results.BadRequest(new { message = "Otel adı, yıldız (1-5) ve puan (0-5) geçerli olmalı." });
    await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Postgres")); await connection.OpenAsync(cancellationToken); await using var command = new NpgsqlCommand("INSERT INTO hotels (name, city_id, district, stars, rating, description) VALUES (@name,@city,@district,@stars,@rating,@description) RETURNING id", connection); command.Parameters.AddWithValue("name", request.Name.Trim()); command.Parameters.AddWithValue("city", request.CityId); command.Parameters.AddWithValue("district", request.District.Trim()); command.Parameters.AddWithValue("stars", request.Stars); command.Parameters.AddWithValue("rating", request.Rating); command.Parameters.AddWithValue("description", request.Description?.Trim() ?? ""); var id = await command.ExecuteScalarAsync(cancellationToken); return Results.Created($"/api/hotels/{id}", new { id, message = "Otel kataloğa eklendi." });
});

app.MapPatch("/api/admin/hotels/{id:guid}/status", async (Guid id, StatusRequest request, HttpRequest httpRequest, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    if (!TryGetSession(httpRequest, sessions, out var session) || session.Role != "admin") return Results.Forbid(); await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Postgres")); await connection.OpenAsync(cancellationToken); await using var command = new NpgsqlCommand("UPDATE hotels SET is_active=@active WHERE id=@id", connection); command.Parameters.AddWithValue("id", id); command.Parameters.AddWithValue("active", request.IsActive); return await command.ExecuteNonQueryAsync(cancellationToken) == 0 ? Results.NotFound() : Results.Ok(new { message = request.IsActive ? "Otel satışa açıldı." : "Otel satışa kapatıldı." });
});

app.MapPatch("/api/admin/fares/{id:guid}", async (Guid id, AdminFareRequest request, HttpRequest httpRequest, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    if (!TryGetSession(httpRequest, sessions, out var session) || session.Role != "admin") return Results.Forbid(); if (request.Price <= 0 || request.SeatsAvailable < 0) return Results.BadRequest(new { message = "Fiyat sıfırdan büyük, koltuk sayısı negatif olamaz." }); await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Postgres")); await connection.OpenAsync(cancellationToken); await using var command = new NpgsqlCommand("UPDATE flight_fares SET price=@price, seats_available=@seats, is_active=@active WHERE id=@id", connection); command.Parameters.AddWithValue("id", id); command.Parameters.AddWithValue("price", request.Price); command.Parameters.AddWithValue("seats", request.SeatsAvailable); command.Parameters.AddWithValue("active", request.IsActive); return await command.ExecuteNonQueryAsync(cancellationToken) == 0 ? Results.NotFound() : Results.Ok(new { message = "Bilet seçeneği güncellendi." });
});

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
    if (term.Length < 2) return Results.Ok(Array.Empty<object>());
    await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Postgres")); await connection.OpenAsync(cancellationToken);
    await using var command = new NpgsqlCommand("SELECT a.iata_code, a.name, c.name, co.name FROM airports a JOIN travel_cities c ON c.id = a.city_id JOIN travel_countries co ON co.id = c.country_id WHERE a.is_active AND (BTRIM(a.iata_code) ILIKE @like OR a.name ILIKE @like OR c.name ILIKE @like) ORDER BY CASE WHEN BTRIM(a.iata_code) ILIKE @prefix THEN 0 WHEN c.name ILIKE @prefix THEN 1 ELSE 2 END, c.name, a.name LIMIT 10", connection);
    command.Parameters.AddWithValue("like", $"%{term}%"); command.Parameters.AddWithValue("prefix", $"{term}%");
    await using var reader = await command.ExecuteReaderAsync(cancellationToken); var results = new List<object>(); while (await reader.ReadAsync(cancellationToken)) results.Add(new { code = reader.GetString(0).Trim(), name = reader.GetString(1), city = reader.GetString(2), country = reader.GetString(3) });
    return Results.Ok(results);
});
app.MapGet("/api/travel/cities", async (IConfiguration configuration, CancellationToken cancellationToken) => { await using var c = new NpgsqlConnection(configuration.GetConnectionString("Postgres")); await c.OpenAsync(cancellationToken); await using var cmd = new NpgsqlCommand("SELECT id,name FROM travel_cities ORDER BY name", c); await using var r = await cmd.ExecuteReaderAsync(cancellationToken); var list = new List<object>(); while (await r.ReadAsync(cancellationToken)) list.Add(new { id = r.GetGuid(0), name = r.GetString(1) }); return Results.Ok(list); });

app.MapGet("/api/hotels/locations", async (string? q, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    var term = (q ?? "").Trim();
    if (term.Length < 2) return Results.Ok(Array.Empty<object>());
    await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Postgres")); await connection.OpenAsync(cancellationToken);
    const string sql = "SELECT 'city:' || c.id, c.name, c.name, NULL::text, 'city' FROM travel_cities c WHERE c.name ILIKE @like AND EXISTS (SELECT 1 FROM hotels h WHERE h.city_id=c.id AND h.is_active) UNION ALL SELECT 'hotel:' || h.id, h.name, c.name, h.district, 'hotel' FROM hotels h JOIN travel_cities c ON c.id=h.city_id WHERE h.is_active AND (h.name ILIKE @like OR c.name ILIKE @like OR h.district ILIKE @like) ORDER BY 5, 2 LIMIT 10";
    await using var command = new NpgsqlCommand(sql, connection); command.Parameters.AddWithValue("like", $"%{term}%");
    await using var reader = await command.ExecuteReaderAsync(cancellationToken); var locations = new List<object>();
    while (await reader.ReadAsync(cancellationToken)) locations.Add(new { key = reader.GetString(0), label = reader.GetString(1), city = reader.GetString(2), district = reader.IsDBNull(3) ? null : reader.GetString(3), type = reader.GetString(4) });
    return Results.Ok(locations);
});

app.MapGet("/api/hotels", async (string? q, DateOnly? checkIn, DateOnly? checkOut, int? adults, int? rooms, int? children, string? childAges, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(q)) return Results.BadRequest(new { message = "Şehir veya otel adı zorunludur." });
    if (checkIn is null || checkOut is null) return Results.BadRequest(new { message = "Giriş ve çıkış tarihleri zorunludur." });
    var arrival = checkIn.GetValueOrDefault(); var departure = checkOut.GetValueOrDefault();
    if (arrival < DateOnly.FromDateTime(DateTime.Now)) return Results.BadRequest(new { message = "Geçmiş tarih için arama yapılamaz." });
    if (departure <= arrival) return Results.BadRequest(new { message = "Çıkış tarihi giriş tarihinden sonra olmalı." });
    if (departure.DayNumber - arrival.DayNumber > 30) return Results.BadRequest(new { message = "Konaklama en fazla 30 gece olabilir." });
    if (adults is not null && (adults < 1 || adults > 20)) return Results.BadRequest(new { message = "Kişi sayısı 1-20 arasında olmalı." });
    if (rooms is not null && (rooms < 1 || rooms > 8)) return Results.BadRequest(new { message = "Oda sayısı 1-8 arasında olmalı." });
    if (children is not null && (children < 0 || children > 8)) return Results.BadRequest(new { message = "Çocuk sayısı 0-8 arasında olmalı." });
    var ages = string.IsNullOrWhiteSpace(childAges) ? Array.Empty<int>() : childAges.Split(',').Select(value => int.TryParse(value, out var age) ? age : -1).ToArray();
    if (ages.Length != (children ?? 0) || ages.Any(age => age is < 0 or > 17)) return Results.BadRequest(new { message = "Çocuk sayısı ile 0-17 arasındaki çocuk yaşları eşleşmeli." });
    if ((adults ?? 1) < (rooms ?? 1)) return Results.BadRequest(new { message = "Her oda için en az bir yetişkin olmalı." });
    var roomCount = rooms ?? 1; var totalGuests = (adults ?? 1) + (children ?? 0);
    await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Postgres")); await connection.OpenAsync(cancellationToken);
    var hotels = await HotelAvailabilityService.SearchAsync(connection, q!, arrival, departure, roomCount, totalGuests, cancellationToken);
    return Results.Ok(hotels);
});

app.MapGet("/api/hotels/{id:guid}/availability", async (Guid id, DateOnly? checkIn, DateOnly? checkOut, int? adults, int? rooms, int? children, string? childAges, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    if (checkIn is null || checkOut is null) return Results.BadRequest(new { message = "Giriş ve çıkış tarihleri zorunludur." });
    var arrival = checkIn.GetValueOrDefault(); var departure = checkOut.GetValueOrDefault();
    if (arrival < DateOnly.FromDateTime(DateTime.Now)) return Results.BadRequest(new { message = "Geçmiş tarih için arama yapılamaz." });
    if (departure <= arrival || departure.DayNumber - arrival.DayNumber > 30) return Results.BadRequest(new { message = "Tarih aralığı geçersizdir." });
    if (adults is null or < 1 or > 20 || rooms is null or < 1 or > 8 || children is null or < 0 or > 8 || adults < rooms) return Results.BadRequest(new { message = "Oda ve misafir bilgileri geçersizdir." });
    var ages = string.IsNullOrWhiteSpace(childAges) ? Array.Empty<int>() : childAges.Split(',').Select(value => int.TryParse(value, out var age) ? age : -1).ToArray();
    if (ages.Length != children || ages.Any(age => age is < 0 or > 17)) return Results.BadRequest(new { message = "Çocuk sayısı ile çocuk yaşları eşleşmelidir." });
    await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Postgres")); await connection.OpenAsync(cancellationToken);
    var hotel = await HotelAvailabilityService.GetByIdAsync(connection, id, arrival, departure, rooms.Value, adults.Value + children.Value, cancellationToken);
    return hotel is null ? Results.NotFound(new { message = "Otel kaldırılmış veya seçilen tarihlerde artık müsait değil." }) : Results.Ok(hotel);
});

app.MapGet("/api/hotels/{id:guid}", async (Guid id, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Postgres")); await connection.OpenAsync(cancellationToken); await using var command = new NpgsqlCommand("SELECT h.id, h.name, c.name, h.district, h.stars, h.rating, h.description, r.id, r.name, r.capacity, r.features FROM hotels h JOIN travel_cities c ON c.id = h.city_id LEFT JOIN hotel_rooms r ON r.hotel_id = h.id AND r.is_active WHERE h.id = @id AND h.is_active ORDER BY r.capacity", connection); command.Parameters.AddWithValue("id", id); await using var reader = await command.ExecuteReaderAsync(cancellationToken); if (!await reader.ReadAsync(cancellationToken)) return Results.NotFound(new { message = "Otel bulunamadı veya satışa kapalı." }); var rooms = new List<object>(); var hotel = new { id = reader.GetGuid(0), name = reader.GetString(1), city = reader.GetString(2), district = reader.GetString(3), stars = reader.GetInt16(4), rating = reader.GetDecimal(5), description = reader.GetString(6) }; do { if (!reader.IsDBNull(7)) rooms.Add(new { id = reader.GetGuid(7), name = reader.GetString(8), capacity = reader.GetInt16(9), features = reader.GetFieldValue<string[]>(10) }); } while (await reader.ReadAsync(cancellationToken)); return Results.Ok(new { hotel, rooms });
});

app.MapGet("/api/flights", async (string? from, string? to, DateOnly? date, DateOnly? returnDate, string? tripType, int? adults, int? children, int? infants, int? passengers, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    var origin = (from ?? "").Trim().ToUpperInvariant(); var destination = (to ?? "").Trim().ToUpperInvariant();
    var journeyType = string.IsNullOrWhiteSpace(tripType) ? "one-way" : tripType.Trim().ToLowerInvariant();
    var adultCount = adults ?? 1; var childCount = children ?? 0; var infantCount = infants ?? 0;
    var seatedPassengers = passengers ?? adultCount + childCount;
    if (origin.Length != 3 || destination.Length != 3) return Results.BadRequest(new { message = "Kalkış ve varış havaalanları zorunludur." });
    if (origin == destination) return Results.BadRequest(new { message = "Kalkış ve varış havaalanları aynı olamaz." });
    if (date is null) return Results.BadRequest(new { message = "Gidiş tarihi zorunludur." });
    if (date < DateOnly.FromDateTime(DateTime.Now)) return Results.BadRequest(new { message = "Geçmiş tarih için uçuş aranamaz." });
    if (journeyType is not ("one-way" or "round-trip")) return Results.BadRequest(new { message = "Yolculuk türü geçersizdir." });
    if (journeyType == "round-trip" && returnDate is null) return Results.BadRequest(new { message = "Gidiş dönüş aramasında dönüş tarihi zorunludur." });
    if (journeyType == "round-trip" && returnDate < date) return Results.BadRequest(new { message = "Dönüş tarihi gidiş tarihinden önce olamaz." });
    if (adultCount is < 1 or > 9 || childCount is < 0 or > 8 || infantCount is < 0 or > 9 || infantCount > adultCount || adultCount + childCount + infantCount > 20) return Results.BadRequest(new { message = "Yetişkin, çocuk ve bebek sayıları geçersizdir." });
    if (seatedPassengers != adultCount + childCount) return Results.BadRequest(new { message = "Koltuk gerektiren yolcu sayısı yetişkin ve çocuk toplamıyla eşleşmelidir." });
    await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Postgres")); await connection.OpenAsync(cancellationToken);
    await using (var airportCommand = new NpgsqlCommand("SELECT COUNT(*) FROM airports WHERE is_active AND BTRIM(iata_code) = ANY(@codes)", connection))
    {
        airportCommand.Parameters.AddWithValue("codes", new[] { origin, destination });
        if (Convert.ToInt64(await airportCommand.ExecuteScalarAsync(cancellationToken)) != 2) return Results.BadRequest(new { message = "Seçilen havaalanlarından biri katalogda bulunmuyor veya aktif değil." });
    }
    var outbound = await FlightSearchService.SearchAsync(connection, origin, destination, date.Value, seatedPassengers, cancellationToken);
    if (journeyType == "one-way")
    {
        return Results.Ok(outbound.Select(flight => new
        {
            id = $"out-{flight.Fare.Id}", tripType = journeyType, totalPrice = flight.Fare.Price,
            currency = flight.Fare.Currency, seatsAvailable = flight.SeatsAvailable, outbound = flight,
            inbound = (FlightItinerary?)null
        }));
    }

    var inbound = await FlightSearchService.SearchAsync(connection, destination, origin, returnDate!.Value, seatedPassengers, cancellationToken);
    var journeys = outbound
        .SelectMany(outFlight => inbound
            .Where(inFlight => inFlight.Fare.Currency == outFlight.Fare.Currency)
            .Select(inFlight => new
            {
                id = $"rt-{outFlight.Fare.Id}-{inFlight.Fare.Id}", tripType = journeyType,
                totalPrice = outFlight.Fare.Price + inFlight.Fare.Price, currency = outFlight.Fare.Currency,
                seatsAvailable = Math.Min(outFlight.SeatsAvailable, inFlight.SeatsAvailable),
                outbound = outFlight, inbound = (FlightItinerary?)inFlight
            }))
        .OrderBy(journey => journey.totalPrice)
        .ThenBy(journey => journey.outbound.DepartureAt)
        .ToArray();
    return Results.Ok(journeys);
});

app.MapPost("/api/flights/fares/{fareId:guid}/reserve", async (Guid fareId, ReserveRequest request, HttpRequest httpRequest, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    if (!TryGetSession(httpRequest, sessions, out _)) return Results.Unauthorized(); if (request.Passengers < 1 || request.Passengers > 20) return Results.BadRequest(new { message = "Yolcu sayısı geçersiz." });
    await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Postgres")); await connection.OpenAsync(cancellationToken); await using var command = new NpgsqlCommand("UPDATE flight_fares SET seats_available = seats_available - @passengers WHERE id=@id AND is_active AND seats_available >= @passengers RETURNING seats_available", connection); command.Parameters.AddWithValue("id", fareId); command.Parameters.AddWithValue("passengers", request.Passengers); var remaining = await command.ExecuteScalarAsync(cancellationToken); return remaining is null ? Results.Conflict(new { message = "Bu bilet seçeneğinde yeterli koltuk kalmadı." }) : Results.Ok(new { message = "Rezervasyon simülasyonu oluşturuldu.", seatsRemaining = (int)remaining });
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
record ReserveRequest(int Passengers);
record AdminHotelRequest(string Name, Guid CityId, string District, int Stars, decimal Rating, string? Description);
record AdminFareRequest(decimal Price, int SeatsAvailable, bool IsActive);
record StatusRequest(bool IsActive);
