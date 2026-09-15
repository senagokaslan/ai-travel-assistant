namespace TravelAssistant.Api.Features.Chat;

internal sealed record ChatMessageRequest(string? Content, Guid ClientMessageId);
