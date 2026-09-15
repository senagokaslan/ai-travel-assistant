namespace TravelAssistant.Api.Features.Auth;

internal sealed record RegisterRequest(string Name, string Email, string Password);
internal sealed record LoginRequest(string Email, string Password);
internal sealed record SessionUser(Guid Id, string Name, string Email, string Role);
