using System.Collections.Concurrent;

namespace TravelAssistant.Api.Features.Auth;

internal sealed class SessionStore
{
    private readonly ConcurrentDictionary<string, SessionUser> sessions = new();

    public string Create(SessionUser user)
    {
        var token = Convert.ToBase64String(global::System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        sessions[token] = user;
        return token;
    }

    public bool TryGet(HttpRequest request, out SessionUser user) => sessions.TryGetValue(GetToken(request), out user!);

    public void Update(HttpRequest request, SessionUser user) => sessions[GetToken(request)] = user;

    public void Remove(HttpRequest request)
    {
        var token = GetToken(request);
        if (!string.IsNullOrWhiteSpace(token)) sessions.TryRemove(token, out _);
    }

    private static string GetToken(HttpRequest request) => request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase).Trim();
}
