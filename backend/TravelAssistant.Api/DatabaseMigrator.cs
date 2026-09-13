using System.Reflection;
using Npgsql;

internal static class DatabaseMigrator
{
    private const string MigrationResourceMarker = ".Database.Migrations.";
    private const long MigrationLockId = 8_404_202_609_120_001;

    public static async Task InitializeAsync(
        IConfiguration configuration,
        bool isDevelopment,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            logger.LogWarning("PostgreSQL bağlantı ayarı olmadığı için migration kontrolü atlandı.");
            return;
        }

        var target = new NpgsqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(target.Database) || string.IsNullOrWhiteSpace(target.Username))
        {
            throw new InvalidOperationException(
                "Postgres bağlantısında veritabanı ve uygulama kullanıcısı belirtilmelidir.");
        }
        var bootstrapEnabled = configuration.GetValue<bool?>("DatabaseBootstrap:Enabled") ?? isDevelopment;

        var canConnect = await CanConnectAsync(target.ConnectionString, cancellationToken);
        var canManageSchema = canConnect && await CanManagePublicSchemaAsync(target.ConnectionString, cancellationToken);
        if (!canConnect || !canManageSchema)
        {
            if (!bootstrapEnabled)
            {
                throw new InvalidOperationException(
                    "PostgreSQL veritabanına bağlanılamadı ve otomatik veritabanı kurulumu kapalı.");
            }

            await BootstrapDatabaseAsync(target, configuration, logger, cancellationToken);
        }

        await ApplyMigrationsAsync(target.ConnectionString, logger, cancellationToken);
    }

    private static async Task<bool> CanConnectAsync(string connectionString, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            return true;
        }
        catch (NpgsqlException)
        {
            return false;
        }
    }

    private static async Task BootstrapDatabaseAsync(
        NpgsqlConnectionStringBuilder target,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var adminConnectionString = configuration.GetConnectionString("PostgresAdmin");
        var admin = string.IsNullOrWhiteSpace(adminConnectionString)
            ? new NpgsqlConnectionStringBuilder(target.ConnectionString)
            : new NpgsqlConnectionStringBuilder(adminConnectionString);

        if (string.IsNullOrWhiteSpace(adminConnectionString))
        {
            admin.Database = configuration["DatabaseBootstrap:MaintenanceDatabase"] ?? "postgres";
            admin.Username = configuration["DatabaseBootstrap:AdminUsername"] ?? "postgres";
        }

        admin.Pooling = false;

        try
        {
            await using var connection = new NpgsqlConnection(admin.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            var roleName = target.Username;
            if (string.IsNullOrWhiteSpace(roleName))
            {
                throw new InvalidOperationException("Postgres bağlantısında uygulama kullanıcısı belirtilmemiş.");
            }

            if (!string.Equals(roleName, admin.Username, StringComparison.Ordinal))
            {
                await EnsureRoleAsync(connection, roleName, target.Password ?? string.Empty, cancellationToken);
            }

            await EnsureDatabaseAsync(connection, target.Database!, roleName, cancellationToken);
            await EnsureSchemaAccessAsync(admin, target.Database!, roleName, cancellationToken);
            logger.LogInformation(
                "PostgreSQL yerel kurulumu hazır: {Database} veritabanı ve {Role} rolü doğrulandı.",
                target.Database,
                roleName);
        }
        catch (NpgsqlException exception)
        {
            throw new InvalidOperationException(
                "Otomatik PostgreSQL kurulumu başarısız. PostgreSQL servisinin çalıştığını ve yerel geliştirmede appsettings.Local.json içindeki parolanın postgres yönetici parolasıyla aynı olduğunu kontrol edin.",
                exception);
        }
    }

    private static async Task EnsureRoleAsync(
        NpgsqlConnection connection,
        string roleName,
        string password,
        CancellationToken cancellationToken)
    {
        await using var existsCommand = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = @role_name)",
            connection);
        existsCommand.Parameters.AddWithValue("role_name", roleName);
        var exists = (bool)(await existsCommand.ExecuteScalarAsync(cancellationToken))!;

        var quotedRole = QuoteIdentifier(roleName);
        var quotedPassword = await QuoteLiteralAsync(connection, password, cancellationToken);
        var sql = exists
            ? $"ALTER ROLE {quotedRole} WITH LOGIN PASSWORD {quotedPassword}"
            : $"CREATE ROLE {quotedRole} WITH LOGIN PASSWORD {quotedPassword}";

        await using var roleCommand = new NpgsqlCommand(sql, connection);
        await roleCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureDatabaseAsync(
        NpgsqlConnection connection,
        string databaseName,
        string ownerName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new InvalidOperationException("Postgres bağlantısında veritabanı adı belirtilmemiş.");
        }

        await using var existsCommand = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = @database_name)",
            connection);
        existsCommand.Parameters.AddWithValue("database_name", databaseName);
        var exists = (bool)(await existsCommand.ExecuteScalarAsync(cancellationToken))!;
        var sql = exists
            ? $"ALTER DATABASE {QuoteIdentifier(databaseName)} OWNER TO {QuoteIdentifier(ownerName)}"
            : $"CREATE DATABASE {QuoteIdentifier(databaseName)} OWNER {QuoteIdentifier(ownerName)}";
        await using var databaseCommand = new NpgsqlCommand(sql, connection);
        await databaseCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> CanManagePublicSchemaAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(
                """
                SELECT has_schema_privilege(current_user, 'public', 'CREATE')
                   AND NOT EXISTS (
                       SELECT 1 FROM pg_tables
                       WHERE schemaname = 'public' AND tableowner <> current_user)
                """,
                connection);
            return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
        }
        catch (NpgsqlException)
        {
            return false;
        }
    }

    private static async Task EnsureSchemaAccessAsync(
        NpgsqlConnectionStringBuilder admin,
        string databaseName,
        string ownerName,
        CancellationToken cancellationToken)
    {
        var databaseAdmin = new NpgsqlConnectionStringBuilder(admin.ConnectionString)
        {
            Database = databaseName,
            Pooling = false
        };
        await using var connection = new NpgsqlConnection(databaseAdmin.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var owner = QuoteIdentifier(ownerName);
        await using var command = new NpgsqlCommand(
            $$"""
            ALTER SCHEMA public OWNER TO {{owner}};
            GRANT ALL ON SCHEMA public TO {{owner}};
            DO $migration_permissions$
            DECLARE object_name text;
            BEGIN
                FOR object_name IN SELECT tablename FROM pg_tables WHERE schemaname = 'public'
                LOOP
                    EXECUTE format('ALTER TABLE public.%I OWNER TO {{owner}}', object_name);
                END LOOP;
                FOR object_name IN SELECT sequencename FROM pg_sequences WHERE schemaname = 'public'
                LOOP
                    EXECUTE format('ALTER SEQUENCE public.%I OWNER TO {{owner}}', object_name);
                END LOOP;
            END
            $migration_permissions$;
            GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO {{owner}};
            GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public TO {{owner}};
            """,
            connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<string> QuoteLiteralAsync(
        NpgsqlConnection connection,
        string value,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT quote_literal(@value)", connection);
        command.Parameters.AddWithValue("value", value);
        return (string)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static string QuoteIdentifier(string value) =>
        $"\"{value.Replace("\"", "\"\"")}\"";

    private static async Task ApplyMigrationsAsync(
        string connectionString,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var lockCommand = new NpgsqlCommand(
            "SELECT pg_advisory_lock(@lock_id)",
            connection);
        lockCommand.Parameters.AddWithValue("lock_id", MigrationLockId);
        await lockCommand.ExecuteNonQueryAsync(cancellationToken);

        try
        {
            await using var tableCommand = new NpgsqlCommand(
                """
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    id text PRIMARY KEY,
                    applied_at timestamptz NOT NULL DEFAULT now()
                )
                """,
                connection);
            await tableCommand.ExecuteNonQueryAsync(cancellationToken);

            foreach (var migration in LoadMigrations())
            {
                if (await IsAppliedAsync(connection, migration.Id, cancellationToken))
                {
                    continue;
                }

                await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
                await using var migrationCommand = new NpgsqlCommand(migration.Sql, connection, transaction);
                await migrationCommand.ExecuteNonQueryAsync(cancellationToken);

                await using var recordCommand = new NpgsqlCommand(
                    "INSERT INTO schema_migrations (id) VALUES (@id)",
                    connection,
                    transaction);
                recordCommand.Parameters.AddWithValue("id", migration.Id);
                await recordCommand.ExecuteNonQueryAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                logger.LogInformation("Veritabanı migration'ı uygulandı: {MigrationId}", migration.Id);
            }
        }
        finally
        {
            await using var unlockCommand = new NpgsqlCommand(
                "SELECT pg_advisory_unlock(@lock_id)",
                connection);
            unlockCommand.Parameters.AddWithValue("lock_id", MigrationLockId);
            await unlockCommand.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    private static async Task<bool> IsAppliedAsync(
        NpgsqlConnection connection,
        string migrationId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM schema_migrations WHERE id = @id)",
            connection);
        command.Parameters.AddWithValue("id", migrationId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static IReadOnlyList<Migration> LoadMigrations()
    {
        var assembly = Assembly.GetExecutingAssembly();
        return assembly.GetManifestResourceNames()
            .Where(name => name.Contains(MigrationResourceMarker, StringComparison.Ordinal) &&
                           name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name =>
            {
                using var stream = assembly.GetManifestResourceStream(name)
                    ?? throw new InvalidOperationException($"Migration kaynağı okunamadı: {name}");
                using var reader = new StreamReader(stream);
                var idStart = name.IndexOf(MigrationResourceMarker, StringComparison.Ordinal) +
                              MigrationResourceMarker.Length;
                var id = name[idStart..^4];
                return new Migration(id, reader.ReadToEnd());
            })
            .ToArray();
    }

    private sealed record Migration(string Id, string Sql);
}
