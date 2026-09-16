using Npgsql;

namespace TravelAssistant.Api.Features.Bookings;

internal static class FlightInventoryAllocator
{
    public static async Task<bool> TryDecreaseFareAsync(NpgsqlConnection connection, Guid fareId, int seats, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "UPDATE flight_fares SET seats_available=seats_available-@seats WHERE id=@fare_id AND is_active AND seats_available>=@seats",
            connection);
        command.Parameters.AddWithValue("fare_id", fareId);
        command.Parameters.AddWithValue("seats", seats);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public static async Task<bool> TryDecreaseLegsAsync(NpgsqlConnection connection, Guid flightId, int seats, int expectedLegs, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "UPDATE flight_legs SET seats_available=seats_available-@seats WHERE flight_id=@flight_id AND seats_available>=@seats",
            connection);
        command.Parameters.AddWithValue("flight_id", flightId);
        command.Parameters.AddWithValue("seats", seats);
        return await command.ExecuteNonQueryAsync(cancellationToken) == expectedLegs;
    }
}
