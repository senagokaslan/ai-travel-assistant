using Npgsql;

namespace TravelAssistant.Api.Features.System;

internal static class SystemEndpoints
{
    public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api", () => Results.Ok(new
        {
            name = "Bağımsız AI Destekli Seyahat Asistanı API",
            description = "Otel ve uçuş arama simülasyonu için başlangıç API'si.",
            documentation = "/swagger",
            health = "/api/health"
        }));

        app.MapGet("/api/health", GetHealthAsync).WithName("GetHealth").WithOpenApi();
        return app;
    }

    private static async Task<IResult> GetHealthAsync(IConfiguration configuration, ILogger<Program> logger, CancellationToken cancellationToken)
    {
        var checkedAt = DateTimeOffset.UtcNow;
        var connectionString = configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Results.Json(new
            {
                status = "unhealthy", checkedAt, api = new { status = "healthy" },
                database = new { status = "unhealthy", message = "PostgreSQL bağlantı ayarı bulunamadı. appsettings.Local.json dosyasını oluşturun veya ConnectionStrings__Postgres ortam değişkenini tanımlayın." }
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT current_database(), current_user", connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken); await reader.ReadAsync(cancellationToken);
            return Results.Ok(new { status = "healthy", checkedAt, api = new { status = "healthy" }, database = new { status = "healthy", name = reader.GetString(0), user = reader.GetString(1) } });
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException or InvalidOperationException)
        {
            logger.LogWarning(exception, "PostgreSQL sağlık kontrolü başarısız oldu.");
            return Results.Json(new
            {
                status = "unhealthy", checkedAt, api = new { status = "healthy" },
                database = new { status = "unhealthy", message = "PostgreSQL'e bağlanılamadı. Veritabanının çalıştığını ve yerel bağlantı ayarlarını kontrol edin." }
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
