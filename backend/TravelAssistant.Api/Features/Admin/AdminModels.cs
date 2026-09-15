namespace TravelAssistant.Api.Features.Admin;

internal sealed record AdminHotelRequest(string Name, Guid CityId, string District, int Stars, decimal Rating, string? Description);
internal sealed record AdminFareRequest(decimal Price, int SeatsAvailable, bool IsActive);
internal sealed record StatusRequest(bool IsActive);
