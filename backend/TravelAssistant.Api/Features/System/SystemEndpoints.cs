using Npgsql;
using TravelAssistant.Api.Infrastructure;

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

    private static async Task<IResult> GetHealthAsync(HttpContext context, IConfiguration configuration, ILogger<Program> logger, ErrorLogThrottle throttle, CancellationToken cancellationToken)
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
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(cancellationToken);
            return Results.Ok(new { status = "healthy", checkedAt, api = new { status = "healthy" }, database = new { status = "healthy" } });
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException or InvalidOperationException)
        {
            if (throttle.ShouldWriteDetails("database_health", TimeSpan.FromMinutes(1)))
                logger.LogWarning(new EventId(5200, "DatabaseHealthFailure"), "PostgreSQL health check failed. TraceId={TraceId} ExceptionType={ExceptionType} SqlState={SqlState}; repeated identical failures are suppressed for one minute.", context.TraceIdentifier, exception.GetType().Name, exception is PostgresException postgres ? postgres.SqlState : null);
            else
                logger.LogDebug(new EventId(5201, "DatabaseHealthFailureRepeated"), "Repeated PostgreSQL health check failure. TraceId={TraceId} ExceptionType={ExceptionType}", context.TraceIdentifier, exception.GetType().Name);
            return Results.Json(new
            {
                status = "unhealthy", checkedAt, api = new { status = "healthy" },
                database = new { status = "unhealthy", message = "PostgreSQL'e bağlanılamadı. Veritabanının çalıştığını ve yerel bağlantı ayarlarını kontrol edin." }
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
