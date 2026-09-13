using Npgsql;

internal static class FlightSearchService
{
    private static readonly TimeSpan MinimumConnection = TimeSpan.FromMinutes(45);
    private static readonly TimeSpan MaximumConnection = TimeSpan.FromHours(6);

    public static async Task<IReadOnlyList<FlightItinerary>> SearchAsync(
        NpgsqlConnection connection,
        string origin,
        string destination,
        DateOnly travelDate,
        int passengers,
        CancellationToken cancellationToken)
    {
        var dayStart = new DateTimeOffset(travelDate.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(3)).UtcDateTime;
        var dayEnd = dayStart.AddDays(1);
        const string sql = """
            SELECT f.id, f.flight_number, al.name, BTRIM(al.iata_code),
                   ff.id, ff.name, ff.price, BTRIM(ff.currency), ff.baggage,
                   ff.change_policy, ff.seats_available,
                   l.id, l.leg_order, BTRIM(dep.iata_code), dep.name,
                   BTRIM(arr.iata_code), arr.name, l.departure_at, l.arrival_at,
                   l.seats_available
            FROM flights f
            JOIN airlines al ON al.id = f.airline_id AND al.is_active
            JOIN flight_fares ff ON ff.flight_id = f.id
                AND ff.is_active AND ff.seats_available >= @passengers
            JOIN flight_legs l ON l.flight_id = f.id
            JOIN airports dep ON dep.id = l.departure_airport_id AND dep.is_active
            JOIN airports arr ON arr.id = l.arrival_airport_id AND arr.is_active
            WHERE f.status = 'scheduled'
              AND EXISTS (
                  SELECT 1
                  FROM flight_legs first_leg
                  JOIN airports first_dep ON first_dep.id = first_leg.departure_airport_id
                  WHERE first_leg.flight_id = f.id
                    AND first_leg.leg_order = (SELECT MIN(x.leg_order) FROM flight_legs x WHERE x.flight_id = f.id)
                    AND BTRIM(first_dep.iata_code) = @origin
                    AND first_leg.departure_at >= @day_start
                    AND first_leg.departure_at < @day_end)
              AND EXISTS (
                  SELECT 1
                  FROM flight_legs last_leg
                  JOIN airports last_arr ON last_arr.id = last_leg.arrival_airport_id
                  WHERE last_leg.flight_id = f.id
                    AND last_leg.leg_order = (SELECT MAX(x.leg_order) FROM flight_legs x WHERE x.flight_id = f.id)
                    AND BTRIM(last_arr.iata_code) = @destination)
            ORDER BY l.departure_at, f.id, ff.price, l.leg_order
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("origin", origin);
        command.Parameters.AddWithValue("destination", destination);
        command.Parameters.AddWithValue("day_start", dayStart);
        command.Parameters.AddWithValue("day_end", dayEnd);
        command.Parameters.AddWithValue("passengers", passengers);

        var rows = new List<FlightRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new FlightRow(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetGuid(4), reader.GetString(5), reader.GetDecimal(6), reader.GetString(7),
                reader.GetString(8), reader.GetString(9), reader.GetInt32(10), reader.GetGuid(11),
                reader.GetInt16(12), reader.GetString(13), reader.GetString(14), reader.GetString(15),
                reader.GetString(16), reader.GetDateTime(17), reader.GetDateTime(18), reader.GetInt32(19)));
        }

        return rows
            .GroupBy(row => new { row.FlightId, row.FareId })
            .Select(group => BuildItinerary(group.OrderBy(row => row.LegOrder).ToArray(), passengers))
            .Where(itinerary => itinerary is not null)
            .Select(itinerary => itinerary!)
            .OrderBy(itinerary => itinerary.DepartureAt)
            .ThenBy(itinerary => itinerary.Fare.Price)
            .ToArray();
    }

    private static FlightItinerary? BuildItinerary(FlightRow[] rows, int passengers)
    {
        if (rows.Length == 0 || rows.Where((row, index) => row.LegOrder != index + 1 || row.LegSeatsAvailable < passengers).Any()) return null;

        for (var index = 0; index < rows.Length - 1; index++)
        {
            var current = rows[index];
            var next = rows[index + 1];
            var connection = next.DepartureAt - current.ArrivalAt;
            if (current.ArrivalCode != next.DepartureCode || connection < MinimumConnection || connection > MaximumConnection) return null;
        }

        var first = rows[0];
        var last = rows[^1];
        var segments = rows.Select(row => new FlightSegment(
            row.LegId, row.LegOrder, row.DepartureCode, row.DepartureAirport,
            row.ArrivalCode, row.ArrivalAirport, row.DepartureAt, row.ArrivalAt,
            (int)(row.ArrivalAt - row.DepartureAt).TotalMinutes, row.LegSeatsAvailable)).ToArray();

        return new FlightItinerary(
            first.FlightId, first.FlightNumber, first.Airline, first.AirlineCode,
            first.DepartureAt, last.ArrivalAt, (int)(last.ArrivalAt - first.DepartureAt).TotalMinutes,
            rows.Length - 1, Math.Min(first.FareSeatsAvailable, rows.Min(row => row.LegSeatsAvailable)),
            new FlightFare(first.FareId, first.FareName, first.Price, first.Currency, first.Baggage, first.ChangePolicy),
            segments);
    }

    private sealed record FlightRow(
        Guid FlightId, string FlightNumber, string Airline, string AirlineCode,
        Guid FareId, string FareName, decimal Price, string Currency, string Baggage,
        string ChangePolicy, int FareSeatsAvailable, Guid LegId, short LegOrder,
        string DepartureCode, string DepartureAirport, string ArrivalCode, string ArrivalAirport,
        DateTime DepartureAt, DateTime ArrivalAt, int LegSeatsAvailable);
}

internal sealed record FlightFare(Guid Id, string Name, decimal Price, string Currency, string Baggage, string ChangePolicy);
internal sealed record FlightSegment(Guid Id, short Order, string From, string FromAirport, string To, string ToAirport, DateTime DepartureAt, DateTime ArrivalAt, int DurationMinutes, int SeatsAvailable);
internal sealed record FlightItinerary(Guid Id, string FlightNumber, string Airline, string AirlineCode, DateTime DepartureAt, DateTime ArrivalAt, int DurationMinutes, int Stops, int SeatsAvailable, FlightFare Fare, IReadOnlyList<FlightSegment> Segments);
