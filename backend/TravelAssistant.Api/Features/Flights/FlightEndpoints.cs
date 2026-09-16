using Npgsql;
using TravelAssistant.Api.Infrastructure;
namespace TravelAssistant.Api.Features.Flights;

internal static class FlightEndpoints
{
    public static IEndpointRouteBuilder MapFlightEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/travel/airports", SearchAirportsAsync);
        app.MapGet("/api/flights", SearchFlightsAsync);
        return app;
    }

    private static async Task<IResult> SearchAirportsAsync(string? q, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var term = (q ?? "").Trim(); if (term.Length < 2) return Results.Ok(Array.Empty<object>());
        await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Postgres")); await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT a.iata_code, a.name, c.name, co.name FROM airports a JOIN travel_cities c ON c.id = a.city_id JOIN travel_countries co ON co.id = c.country_id WHERE a.is_active AND (BTRIM(a.iata_code) ILIKE @like OR a.name ILIKE @like OR c.name ILIKE @like) ORDER BY CASE WHEN BTRIM(a.iata_code) ILIKE @prefix THEN 0 WHEN c.name ILIKE @prefix THEN 1 ELSE 2 END, c.name, a.name LIMIT 10", connection);
        command.Parameters.AddWithValue("like", $"%{term}%"); command.Parameters.AddWithValue("prefix", $"{term}%");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken); var results = new List<object>();
        while (await reader.ReadAsync(cancellationToken)) results.Add(new { code = reader.GetString(0).Trim(), name = reader.GetString(1), city = reader.GetString(2), country = reader.GetString(3) });
        return Results.Ok(results);
    }

    private static async Task<IResult> SearchFlightsAsync(string? from, string? to, DateOnly? date, DateOnly? returnDate, string? tripType, int? adults, int? children, int? infants, int? passengers, HttpContext context, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var origin = (from ?? "").Trim().ToUpperInvariant(); var destination = (to ?? "").Trim().ToUpperInvariant();
        var journeyType = string.IsNullOrWhiteSpace(tripType) ? "one-way" : tripType.Trim().ToLowerInvariant();
        var adultCount = adults ?? 1; var childCount = children ?? 0; var infantCount = infants ?? 0; var seatedPassengers = passengers ?? adultCount + childCount; var travelerCount = adultCount + childCount + infantCount;
        if (origin.Length != 3 || destination.Length != 3) return Invalid(context, "invalid_route", "Kalkış ve varış havaalanlarını seçin.", "Her iki alanı da öneri listesinden seçip tekrar arayın.");
        if (origin == destination) return Invalid(context, "same_location", "Kalkış ve varış aynı olamaz.", "Farklı bir varış havaalanı seçin.");
        if (date is null) return Invalid(context, "invalid_date", "Gidiş tarihi zorunludur.", "Bugün veya daha ileri bir tarih seçin.");
        if (date < DateOnly.FromDateTime(DateTime.Now)) return Invalid(context, "invalid_date", "Geçmiş tarih için uçuş aranamaz.", "Bugün veya daha ileri bir tarih seçin.");
        if (journeyType is not ("one-way" or "round-trip")) return Invalid(context, "invalid_trip_type", "Yolculuk türü geçersizdir.", "Tek yön veya gidiş dönüş seçeneklerinden birini seçin.");
        if (journeyType == "round-trip" && returnDate is null) return Invalid(context, "invalid_date", "Gidiş dönüş aramasında dönüş tarihi zorunludur.", "Gidiş tarihinden önce olmayan bir dönüş tarihi seçin.");
        if (journeyType == "round-trip" && returnDate < date) return Invalid(context, "invalid_date", "Dönüş tarihi gidiş tarihinden önce olamaz.", "Dönüş tarihini gidiş tarihiyle aynı gün veya sonrasına alın.");
        if (adultCount is < 1 or > 9 || childCount is < 0 or > 8 || infantCount is < 0 or > 9 || infantCount > adultCount || adultCount + childCount + infantCount > 20) return Invalid(context, "invalid_passengers", "Yolcu sayıları geçersizdir.", "En az bir yetişkin seçin ve bebek sayısını yetişkin sayısını aşmayacak şekilde düzenleyin.");
        if (seatedPassengers != adultCount + childCount) return Invalid(context, "invalid_passengers", "Koltuk gerektiren yolcu sayısı geçersizdir.", "Yetişkin ve çocuk sayılarını kontrol edip tekrar deneyin.");
        await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Postgres")); await connection.OpenAsync(cancellationToken);
        await using (var airportCommand = new NpgsqlCommand("SELECT COUNT(*) FROM airports WHERE is_active AND BTRIM(iata_code) = ANY(@codes)", connection))
        {
            airportCommand.Parameters.AddWithValue("codes", new[] { origin, destination });
            if (Convert.ToInt64(await airportCommand.ExecuteScalarAsync(cancellationToken)) != 2) return ApiErrorResults.Create(context, 404, "location_not_found", "Seçilen havaalanlarından biri bulunamadı veya uçuşa kapalı.", "Kalkış ve varışı öneri listesinden yeniden seçin.");
        }
        var outbound = await FlightSearchService.SearchAsync(connection, origin, destination, date.Value, seatedPassengers, cancellationToken);
        if (journeyType == "one-way")
            return Results.Ok(outbound.Select(flight => new { id = $"out-{flight.Fare.Id}", tripType = journeyType, pricePerTraveler = flight.Fare.Price, totalPrice = flight.Fare.Price * travelerCount, travelerCount, currency = flight.Fare.Currency, seatsAvailable = flight.SeatsAvailable, outbound = flight, inbound = (FlightItinerary?)null }));

        var inbound = await FlightSearchService.SearchAsync(connection, destination, origin, returnDate!.Value, seatedPassengers, cancellationToken);
        var journeys = outbound.SelectMany(outFlight => inbound.Where(inFlight => inFlight.Fare.Currency == outFlight.Fare.Currency).Select(inFlight => new
        {
            id = $"rt-{outFlight.Fare.Id}-{inFlight.Fare.Id}", tripType = journeyType, pricePerTraveler = outFlight.Fare.Price + inFlight.Fare.Price,
            totalPrice = (outFlight.Fare.Price + inFlight.Fare.Price) * travelerCount, travelerCount, currency = outFlight.Fare.Currency,
            seatsAvailable = Math.Min(outFlight.SeatsAvailable, inFlight.SeatsAvailable), outbound = outFlight, inbound = (FlightItinerary?)inFlight
        })).OrderBy(journey => journey.totalPrice).ThenBy(journey => journey.outbound.DepartureAt).ToArray();
        return Results.Ok(journeys);
    }

    private static IResult Invalid(HttpContext context, string code, string message, string action) => ApiErrorResults.Create(context, 400, code, message, action);

}
