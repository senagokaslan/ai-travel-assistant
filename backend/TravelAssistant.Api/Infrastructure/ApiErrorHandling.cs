using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Npgsql;

namespace TravelAssistant.Api.Infrastructure;

internal sealed record ApiErrorDefinition(int Status, string Code, string Message, string Action);

internal static class ApiErrorCatalog
{
    public static readonly ApiErrorDefinition InvalidRequest = new(400, "invalid_request", "Gönderilen bilgiler geçerli değil.", "İşaretli alanları kontrol edip tekrar deneyin.");
    public static readonly ApiErrorDefinition DatabaseUnavailable = new(503, "database_unavailable", "Seyahat verilerine şu anda ulaşılamıyor.", "Biraz bekleyip tekrar deneyin. Sorun sürerse takip kodunu geliştiriciyle paylaşın.");
    public static readonly ApiErrorDefinition ServiceTimeout = new(503, "service_timeout", "İşlem beklenenden uzun sürdü ve güvenli biçimde durduruldu.", "Biraz bekleyip tekrar deneyin.");
    public static readonly ApiErrorDefinition DependencyUnavailable = new(503, "dependency_unavailable", "Gerekli servislerden biri şu anda yanıt vermiyor.", "Biraz bekleyip tekrar deneyin veya klasik arama formunu kullanın.");
    public static readonly ApiErrorDefinition Unexpected = new(500, "unexpected_error", "İşlem tamamlanırken beklenmeyen bir sorun oluştu.", "Tekrar deneyin. Sorun sürerse takip kodunu geliştiriciyle paylaşın.");
}

internal sealed class SafeApiException(ApiErrorDefinition error) : Exception
{
    public ApiErrorDefinition Error { get; } = error;
}

internal sealed class ErrorLogThrottle
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> detailedLogs = new(StringComparer.Ordinal);

    public bool ShouldWriteDetails(string key, TimeSpan? interval = null)
    {
        var now = DateTimeOffset.UtcNow;
        var window = interval ?? TimeSpan.FromSeconds(30);
        while (true)
        {
            if (!detailedLogs.TryGetValue(key, out var previous)) return detailedLogs.TryAdd(key, now);
            if (now - previous < window) return false;
            if (detailedLogs.TryUpdate(key, now, previous)) return true;
        }
    }
}

internal sealed class ApiErrorHandlingMiddleware(RequestDelegate next, ILogger<ApiErrorHandlingMiddleware> logger, ErrorLogThrottle throttle)
{
    private static readonly EventId ApiFailureEvent = new(5000, "ApiFailure");
    private static readonly EventId ApiFailureDetailEvent = new(5001, "ApiFailureDetail");

    public async Task InvokeAsync(HttpContext context)
    {
        var traceId = Guid.NewGuid().ToString("N");
        context.TraceIdentifier = traceId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Trace-Id"] = traceId;
            return Task.CompletedTask;
        });

        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            logger.LogInformation(new EventId(4990, "RequestCancelled"), "Request cancelled by client. TraceId={TraceId} Method={Method} Path={Path}", traceId, context.Request.Method, SanitizePath(context.Request.Path.Value));
        }
        catch (Exception exception)
        {
            var error = Map(exception);
            LogSafely(context, traceId, error, exception);
            if (context.Response.HasStarted) throw;
            context.Response.Clear();
            context.Response.StatusCode = error.Status;
            await context.Response.WriteAsJsonAsync(new { error.Code, error.Message, error.Action, traceId }, CancellationToken.None);
        }
    }

    private void LogSafely(HttpContext context, string traceId, ApiErrorDefinition error, Exception exception)
    {
        var exceptionType = exception.GetType().Name;
        var path = SanitizePath(context.Request.Path.Value);
        var sqlState = exception is PostgresException postgres ? postgres.SqlState : null;
        if (error.Status < 500)
        {
            logger.LogInformation(new EventId(4000, "ExpectedApiFailure"),
                "Expected API failure. TraceId={TraceId} Code={Code} ExceptionType={ExceptionType} Method={Method} Path={Path}",
                traceId, error.Code, exceptionType, context.Request.Method, path);
            return;
        }
        logger.LogWarning(ApiFailureEvent,
            "API failure. TraceId={TraceId} Code={Code} ExceptionType={ExceptionType} Method={Method} Path={Path} SqlState={SqlState}",
            traceId, error.Code, exceptionType, context.Request.Method, path, sqlState);

        var signature = $"{error.Code}|{exceptionType}|{context.Request.Method}|{path}";
        if (throttle.ShouldWriteDetails(signature))
        {
            logger.LogError(ApiFailureDetailEvent,
                "API failure detail. TraceId={TraceId} Code={Code} ExceptionType={ExceptionType} Method={Method} Path={Path} SqlState={SqlState} StackTrace={StackTrace}",
                traceId, error.Code, exceptionType, context.Request.Method, path, sqlState, SanitizeStackTrace(exception.StackTrace));
        }
    }

    private static ApiErrorDefinition Map(Exception exception) => exception switch
    {
        SafeApiException safe => safe.Error,
        BadHttpRequestException => ApiErrorCatalog.InvalidRequest,
        NpgsqlException => ApiErrorCatalog.DatabaseUnavailable,
        ArgumentException argument when IsNpgsqlFailure(argument) => ApiErrorCatalog.DatabaseUnavailable,
        InvalidOperationException invalid when IsNpgsqlFailure(invalid) => ApiErrorCatalog.DatabaseUnavailable,
        TimeoutException => ApiErrorCatalog.ServiceTimeout,
        OperationCanceledException => ApiErrorCatalog.ServiceTimeout,
        HttpRequestException => ApiErrorCatalog.DependencyUnavailable,
        _ => ApiErrorCatalog.Unexpected
    };

    private static bool IsNpgsqlFailure(Exception exception) => exception.StackTrace?.Contains("Npgsql.", StringComparison.Ordinal) == true;

    private static string? SanitizeStackTrace(string? stackTrace) => string.IsNullOrWhiteSpace(stackTrace)
        ? stackTrace
        : Regex.Replace(stackTrace, @" in [^\r\n]+:line \d+", " [source path removed]", RegexOptions.CultureInvariant);

    private static string SanitizePath(string? path)
    {
        var value = string.IsNullOrWhiteSpace(path) ? "/" : path;
        value = Regex.Replace(value, @"(?i)[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}", "{id}", RegexOptions.CultureInvariant);
        return Regex.Replace(value, @"(?<=/)\d+(?=/|$)", "{number}", RegexOptions.CultureInvariant);
    }
}

internal static class ApiErrorResults
{
    public static IResult Create(HttpContext context, int status, string code, string message, string action, object? extra = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["code"] = code,
            ["message"] = message,
            ["action"] = action,
            ["traceId"] = context.TraceIdentifier
        };
        if (extra is not null)
            foreach (var property in extra.GetType().GetProperties()) payload[property.Name] = property.GetValue(extra);
        return Results.Json(payload, statusCode: status);
    }
}
