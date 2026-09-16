using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Npgsql;
using TravelAssistant.Api.Infrastructure;

namespace TravelAssistant.Api.Features.Auth;

internal sealed class SessionStore
{
    private static readonly TimeSpan IdleLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromHours(8);
    private readonly ConcurrentDictionary<string, SessionEntry> sessions = new(StringComparer.Ordinal);

    public string Create(SessionUser user)
    {
        RemoveExpired();
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var now = DateTimeOffset.UtcNow;
        sessions[HashToken(token)] = new SessionEntry(user, now, now);
        return token;
    }

    public async Task<SessionUser?> GetValidUserAsync(HttpRequest request, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var key = GetTokenHash(request);
        if (key is null || !sessions.TryGetValue(key, out var entry)) return null;

        var now = DateTimeOffset.UtcNow;
        if (now - entry.LastSeenAt > IdleLifetime || now - entry.CreatedAt > AbsoluteLifetime)
        {
            sessions.TryRemove(key, out _);
            return null;
        }

        var connectionString = configuration.GetConnectionString("Postgres");
        if (string.IsNullOrWhiteSpace(connectionString)) throw new SafeApiException(ApiErrorCatalog.DatabaseUnavailable);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT id, name, email, role FROM app_users WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", entry.User.Id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            sessions.TryRemove(key, out _);
            return null;
        }

        var currentUser = new SessionUser(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
        sessions.TryUpdate(key, entry with { User = currentUser, LastSeenAt = now }, entry);
        return currentUser;
    }

    public void Update(HttpRequest request, SessionUser user)
    {
        var key = GetTokenHash(request);
        if (key is not null && sessions.TryGetValue(key, out var entry))
            sessions.TryUpdate(key, entry with { User = user, LastSeenAt = DateTimeOffset.UtcNow }, entry);
    }

    public void Remove(HttpRequest request)
    {
        var key = GetTokenHash(request);
        if (key is not null) sessions.TryRemove(key, out _);
    }

    private void RemoveExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in sessions)
            if (now - pair.Value.LastSeenAt > IdleLifetime || now - pair.Value.CreatedAt > AbsoluteLifetime)
                sessions.TryRemove(pair.Key, out _);
    }

    private static string? GetTokenHash(HttpRequest request)
    {
        if (!AuthenticationHeaderValue.TryParse(request.Headers.Authorization.ToString(), out var header) ||
            !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(header.Parameter)) return null;
        return HashToken(header.Parameter);
    }

    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private sealed record SessionEntry(SessionUser User, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt);
}
