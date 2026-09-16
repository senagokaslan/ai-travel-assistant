using System.Text.Json;
using Npgsql;
using Xunit;

namespace TravelAssistant.Api.Tests;

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresTestDatabase>
{
    public const string Name = "isolated-postgres";
}

public sealed class PostgresTestDatabase : IAsyncLifetime
{
    private string adminConnectionString = "";
    private string schema = "";

    public Guid HotelId { get; } = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public Guid RoomId { get; } = Guid.Parse("10000000-0000-0000-0000-000000000002");
    public Guid FlightId { get; } = Guid.Parse("20000000-0000-0000-0000-000000000001");
    public Guid FareId { get; } = Guid.Parse("20000000-0000-0000-0000-000000000002");
    public Guid RaceFareId { get; } = Guid.Parse("20000000-0000-0000-0000-000000000003");
    public Guid OwnerId { get; } = Guid.Parse("30000000-0000-0000-0000-000000000001");
    public Guid OtherUserId { get; } = Guid.Parse("30000000-0000-0000-0000-000000000002");
    public Guid BookingId { get; } = Guid.Parse("30000000-0000-0000-0000-000000000003");
    public Guid ConversationId { get; } = Guid.Parse("40000000-0000-0000-0000-000000000001");
    public DateOnly TravelDate { get; } = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10);
    public string ConnectionString => new NpgsqlConnectionStringBuilder(adminConnectionString) { SearchPath = schema }.ConnectionString;

    public async Task InitializeAsync()
    {
        adminConnectionString = LoadConnectionString();
        schema = $"test_{Guid.NewGuid():N}";
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", connection))
            await create.ExecuteNonQueryAsync();

        await using var isolated = new NpgsqlConnection(ConnectionString);
        await isolated.OpenAsync();
        await CreateSchemaAsync(isolated);
        await SeedAsync(isolated);
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(schema)) return;
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", connection);
        await drop.ExecuteNonQueryAsync();
    }

    public async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static string LoadConnectionString()
    {
        var environment = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");
        if (!string.IsNullOrWhiteSpace(environment)) return environment;

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TravelAssistant.slnx"))) directory = directory.Parent;
        if (directory is null) throw new InvalidOperationException("Test veritabanı bağlantısı bulunamadı. ConnectionStrings__Postgres ortam değişkenini tanımlayın.");
        var path = Path.Combine(directory.FullName, "backend", "TravelAssistant.Api", "appsettings.Local.json");
        if (!File.Exists(path)) throw new InvalidOperationException("Test veritabanı bağlantısı bulunamadı. ConnectionStrings__Postgres ortam değişkenini tanımlayın.");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("ConnectionStrings").GetProperty("Postgres").GetString()
            ?? throw new InvalidOperationException("PostgreSQL bağlantısı boş olamaz.");
    }

    private static async Task CreateSchemaAsync(NpgsqlConnection connection)
    {
        const string sql = """
            CREATE TABLE travel_cities (id uuid PRIMARY KEY, name text NOT NULL);
            CREATE TABLE hotels (id uuid PRIMARY KEY, name text NOT NULL, city_id uuid NOT NULL, district text NOT NULL, stars smallint NOT NULL, rating numeric(2,1) NOT NULL, description text NOT NULL, board_types text[] NOT NULL, cancellation_policy text NOT NULL, is_active boolean NOT NULL);
            CREATE TABLE hotel_rooms (id uuid PRIMARY KEY, hotel_id uuid NOT NULL, name text NOT NULL, capacity smallint NOT NULL, features text[] NOT NULL, is_active boolean NOT NULL);
            CREATE TABLE room_daily_rates (room_id uuid NOT NULL, stay_date date NOT NULL, nightly_price numeric(10,2) NOT NULL, rooms_available integer NOT NULL, PRIMARY KEY (room_id, stay_date));
            CREATE TABLE hotel_room_holds (room_id uuid NOT NULL, check_in date NOT NULL, check_out date NOT NULL, quantity integer NOT NULL, status text NOT NULL, held_until timestamptz);

            CREATE TABLE airports (id uuid PRIMARY KEY, city_id uuid NOT NULL, name text NOT NULL, iata_code char(3) NOT NULL, is_active boolean NOT NULL);
            CREATE TABLE airlines (id uuid PRIMARY KEY, name text NOT NULL, iata_code char(2) NOT NULL, is_active boolean NOT NULL);
            CREATE TABLE flights (id uuid PRIMARY KEY, flight_number text NOT NULL, airline_id uuid NOT NULL, status text NOT NULL);
            CREATE TABLE flight_legs (id uuid PRIMARY KEY, flight_id uuid NOT NULL, leg_order smallint NOT NULL, departure_airport_id uuid NOT NULL, arrival_airport_id uuid NOT NULL, departure_at timestamptz NOT NULL, arrival_at timestamptz NOT NULL, seats_available integer NOT NULL);
            CREATE TABLE flight_fares (id uuid PRIMARY KEY, flight_id uuid NOT NULL, name text NOT NULL, price numeric(10,2) NOT NULL, currency char(3) NOT NULL, baggage text NOT NULL, change_policy text NOT NULL, seats_available integer NOT NULL, is_active boolean NOT NULL);

            CREATE TABLE chat_conversations (id uuid PRIMARY KEY, context jsonb NOT NULL DEFAULT '{}'::jsonb, updated_at timestamptz NOT NULL DEFAULT now());
            CREATE TABLE app_bookings (id uuid PRIMARY KEY, user_id uuid NOT NULL, request_key uuid, reference_code text, kind text NOT NULL, title text NOT NULL, status text NOT NULL, total_price numeric(12,2), currency char(3), details jsonb, created_at timestamptz NOT NULL DEFAULT now(), confirmed_at timestamptz, cancelled_at timestamptz);
            CREATE UNIQUE INDEX ux_app_bookings_user_request ON app_bookings (user_id, request_key) WHERE request_key IS NOT NULL;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task SeedAsync(NpgsqlConnection connection)
    {
        var cityId = Guid.Parse("10000000-0000-0000-0000-000000000010");
        var originId = Guid.Parse("20000000-0000-0000-0000-000000000010");
        var destinationId = Guid.Parse("20000000-0000-0000-0000-000000000011");
        var airlineId = Guid.Parse("20000000-0000-0000-0000-000000000012");
        var legId = Guid.Parse("20000000-0000-0000-0000-000000000013");
        const string sql = """
            INSERT INTO travel_cities VALUES (@city_id, 'TestCity');
            INSERT INTO hotels VALUES (@hotel_id, 'Deterministik Otel', @city_id, 'Merkez', 4, 4.5, 'Test oteli', ARRAY['Kahvaltı'], 'Ücretsiz iptal', true);
            INSERT INTO hotel_rooms VALUES (@room_id, @hotel_id, 'Standart Oda', 2, ARRAY['Wi-Fi'], true);
            INSERT INTO room_daily_rates
            SELECT @room_id, @travel_date + day, 1000, 1 FROM generate_series(0, 4) AS day;

            INSERT INTO airports VALUES (@origin_id, @city_id, 'Başlangıç Havalimanı', 'AAA', true), (@destination_id, @city_id, 'Varış Havalimanı', 'BBB', true);
            INSERT INTO airlines VALUES (@airline_id, 'Test Hava Yolları', 'TT', true);
            INSERT INTO flights VALUES (@flight_id, 'TT100', @airline_id, 'scheduled');
            INSERT INTO flight_legs VALUES (@leg_id, @flight_id, 1, @origin_id, @destination_id, @departure_at, @arrival_at, 3);
            INSERT INTO flight_fares VALUES (@fare_id, @flight_id, 'Standart', 1500, 'TRY', '15 kg', 'Değişiklik ücretli', 3, true);
            INSERT INTO flight_fares VALUES (@race_fare_id, @flight_id, 'Son Koltuk', 1700, 'TRY', '15 kg', 'Değişiklik ücretli', 1, true);

            INSERT INTO chat_conversations (id) VALUES (@conversation_id);
            INSERT INTO app_bookings (id, user_id, reference_code, kind, title, status, total_price, currency, details, confirmed_at) VALUES (@booking_id, @owner_id, 'TEST-001', 'hotel', 'Deterministik Otel', 'simulated', 3000, 'TRY', '{}'::jsonb, now());
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("city_id", cityId);
        command.Parameters.AddWithValue("hotel_id", HotelId);
        command.Parameters.AddWithValue("room_id", RoomId);
        command.Parameters.AddWithValue("travel_date", TravelDate);
        command.Parameters.AddWithValue("origin_id", originId);
        command.Parameters.AddWithValue("destination_id", destinationId);
        command.Parameters.AddWithValue("airline_id", airlineId);
        command.Parameters.AddWithValue("flight_id", FlightId);
        command.Parameters.AddWithValue("leg_id", legId);
        command.Parameters.AddWithValue("departure_at", TravelDate.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc));
        command.Parameters.AddWithValue("arrival_at", TravelDate.ToDateTime(new TimeOnly(10, 15), DateTimeKind.Utc));
        command.Parameters.AddWithValue("fare_id", FareId);
        command.Parameters.AddWithValue("race_fare_id", RaceFareId);
        command.Parameters.AddWithValue("conversation_id", ConversationId);
        command.Parameters.AddWithValue("booking_id", BookingId);
        command.Parameters.AddWithValue("owner_id", OwnerId);
        await command.ExecuteNonQueryAsync();
    }
}
