using System.Text.Json;
using TravelAssistant.Api.Features.Chat;
using TravelAssistant.Api.Features.Flights;
using TravelAssistant.Api.Features.Hotels;

namespace TravelAssistant.Api.Tests;

[Collection(PostgresCollection.Name)]
public sealed class SearchAndConversationTests(PostgresTestDatabase database)
{
    [Fact]
    public async Task Hotel_search_returns_only_a_complete_available_stay()
    {
        await using var connection = await database.OpenConnectionAsync();

        var results = await HotelAvailabilityService.SearchAsync(
            connection, "TestCity", database.TravelDate, database.TravelDate.AddDays(3), 1, 2, CancellationToken.None);

        var hotel = Assert.Single(results);
        Assert.Equal(database.HotelId, hotel.Id);
        Assert.Equal(3000m, hotel.TotalPrice);
        var option = Assert.Single(hotel.Options);
        Assert.Equal(2, option.TotalCapacity);
        Assert.Equal(3, Assert.Single(option.Rooms).Nights.Length);
    }

    [Fact]
    public async Task Flight_search_respects_route_date_and_capacity()
    {
        await using var connection = await database.OpenConnectionAsync();

        var available = await FlightSearchService.SearchAsync(connection, "AAA", "BBB", database.TravelDate, 3, CancellationToken.None);
        var unavailable = await FlightSearchService.SearchAsync(connection, "AAA", "BBB", database.TravelDate, 4, CancellationToken.None);

        var itinerary = Assert.Single(available, item => item.Fare.Id == database.FareId);
        Assert.Equal(database.FlightId, itinerary.Id);
        Assert.Equal(3, itinerary.SeatsAvailable);
        Assert.Empty(unavailable);
    }

    [Fact]
    public async Task Conversation_keeps_previous_location_and_completes_missing_hotel_data()
    {
        await using var connection = await database.OpenConnectionAsync();
        var fallback = AiUnderstandingOutcome.Fallback("test");

        var first = await TravelChatService.ReplyAsync(connection, database.ConversationId, "TestCity için otel bak", fallback, CancellationToken.None);
        Assert.Contains("tarih", first.Content, StringComparison.OrdinalIgnoreCase);

        var secondMessage = $"{database.TravelDate:dd.MM.yyyy} üç gece iki yetişkin bir oda";
        var second = await TravelChatService.ReplyAsync(connection, database.ConversationId, secondMessage, fallback, CancellationToken.None);
        var context = JsonSerializer.Deserialize<ChatContext>(second.ContextJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(context);
        Assert.Equal("hotel", context.Intent);
        Assert.Equal("TestCity", context.HotelLocation);
        Assert.Equal(database.TravelDate, context.CheckIn);
        Assert.Equal(database.TravelDate.AddDays(3), context.CheckOut);
        Assert.Equal(2, context.Adults);
        Assert.Equal(1, context.Rooms);
        Assert.True(context.HasSearchBase);
    }
}
