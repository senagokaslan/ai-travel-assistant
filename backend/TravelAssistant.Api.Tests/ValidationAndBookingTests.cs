using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Npgsql;
using TravelAssistant.Api.Features.Bookings;
using TravelAssistant.Api.Features.Flights;
using TravelAssistant.Api.Features.Hotels;

namespace TravelAssistant.Api.Tests;

[Collection(PostgresCollection.Name)]
public sealed class ValidationAndBookingTests(PostgresTestDatabase database)
{
    private static readonly IConfiguration EmptyConfiguration = new ConfigurationBuilder().Build();

    [Fact]
    public async Task Searches_reject_past_dates_and_same_locations_before_database_access()
    {
        var hotelContext = new DefaultHttpContext();
        var hotelResult = await HotelEndpoints.SearchHotelsAsync(
            "TestCity", DateOnly.FromDateTime(DateTime.Now).AddDays(-1), DateOnly.FromDateTime(DateTime.Now).AddDays(1),
            1, 1, 0, null, hotelContext, EmptyConfiguration, CancellationToken.None);

        var flightContext = new DefaultHttpContext();
        var flightResult = await FlightEndpoints.SearchFlightsAsync(
            "AAA", "AAA", DateOnly.FromDateTime(DateTime.Now).AddDays(1), null, "one-way",
            1, 0, 0, 1, flightContext, EmptyConfiguration, CancellationToken.None);

        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(hotelResult).StatusCode);
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsAssignableFrom<IStatusCodeHttpResult>(flightResult).StatusCode);
    }

    [Fact]
    public async Task Searches_return_not_found_for_unknown_locations()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = database.ConnectionString })
            .Build();
        var flightResult = await FlightEndpoints.SearchFlightsAsync(
            "XXX", "BBB", database.TravelDate, null, "one-way", 1, 0, 0, 1,
            new DefaultHttpContext(), configuration, CancellationToken.None);
        var hotelResult = await HotelEndpoints.SearchHotelsAsync(
            "OlmayanŞehir", database.TravelDate, database.TravelDate.AddDays(1), 1, 1, 0, null,
            new DefaultHttpContext(), configuration, CancellationToken.None);

        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(flightResult).StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsAssignableFrom<IStatusCodeHttpResult>(hotelResult).StatusCode);
    }

    [Fact]
    public void Valid_travelers_and_contact_are_accepted_for_a_booking()
    {
        var travelers = new[]
        {
            new BookingTravelerRequest("adult", "Ayşe", "Yılmaz", null, null),
            new BookingTravelerRequest("child", "Deniz", "Yılmaz", 8, null)
        };

        var result = BookingPeopleValidator.Validate(
            travelers, new BookingContactRequest(0, "ayse@example.test", "+90 555 111 22 33"),
            adults: 1, children: 1, infants: 0, expectedChildAges: new[] { 8 }, kind: "hotel");

        Assert.True(result.IsValid);
        Assert.Equal("+905551112233", result.Value!.Contact.Phone);
    }

    [Fact]
    public async Task Booking_creation_writes_a_unique_reference_and_expected_total()
    {
        await using var connection = await database.OpenConnectionAsync();
        var requestKey = Guid.NewGuid();

        var booking = await BookingConfirmationEndpoints.InsertBookingAsync(
            connection, database.OwnerId, requestKey, "hotel", "Deterministik Otel", 3000m, "TRY",
            new { source = "automated-test" }, CancellationToken.None);

        Assert.StartsWith("SIM-", booking.ReferenceCode);
        Assert.Equal(3000m, booking.TotalPrice);
        await using var count = new NpgsqlCommand("SELECT COUNT(*) FROM app_bookings WHERE user_id=@user_id AND request_key=@request_key", connection);
        count.Parameters.AddWithValue("user_id", database.OwnerId);
        count.Parameters.AddWithValue("request_key", requestKey);
        Assert.Equal(1L, await count.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Another_user_cannot_read_the_booking()
    {
        await using var connection = await database.OpenConnectionAsync();

        var ownerResult = await BookingManagementEndpoints.FindOwnedAsync(connection, database.OwnerId, database.BookingId, false, CancellationToken.None);
        var otherResult = await BookingManagementEndpoints.FindOwnedAsync(connection, database.OtherUserId, database.BookingId, false, CancellationToken.None);

        Assert.NotNull(ownerResult);
        Assert.Null(otherResult);
    }

    [Fact]
    public async Task Two_simultaneous_requests_cannot_take_the_same_last_seat()
    {
        async Task<bool> AttemptAsync()
        {
            await using var connection = await database.OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            var allocated = await FlightInventoryAllocator.TryDecreaseFareAsync(connection, database.RaceFareId, 1, CancellationToken.None);
            await transaction.CommitAsync();
            return allocated;
        }

        var outcomes = await Task.WhenAll(Task.Run(AttemptAsync), Task.Run(AttemptAsync));

        Assert.Single(outcomes, allocated => allocated);
        Assert.Single(outcomes, allocated => !allocated);
        await using var verify = await database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("SELECT seats_available FROM flight_fares WHERE id=@id", verify);
        command.Parameters.AddWithValue("id", database.RaceFareId);
        Assert.Equal(0, await command.ExecuteScalarAsync());
    }
}
