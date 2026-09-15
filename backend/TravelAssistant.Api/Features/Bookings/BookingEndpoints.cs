using Npgsql;
using TravelAssistant.Api.Features.Flights;
using TravelAssistant.Api.Features.Hotels;

namespace TravelAssistant.Api.Features.Bookings;

internal static class BookingEndpoints
{
    public static IEndpointRouteBuilder MapBookingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/bookings/summary/hotel", BuildHotelSummaryAsync);
        app.MapPost("/api/bookings/summary/flight", BuildFlightSummaryAsync);
        return app;
    }

    private static async Task<IResult> BuildHotelSummaryAsync(HotelBookingSummaryRequest request, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (request.HotelId == Guid.Empty || string.IsNullOrWhiteSpace(request.OptionKey) || request.OptionKey.Length > 1_000) return BadRequest("Seçilen otel ve oda seçeneği zorunludur.");
        if (request.CheckIn < today || request.CheckOut <= request.CheckIn || request.CheckOut.DayNumber - request.CheckIn.DayNumber > 30) return BadRequest("Konaklama tarihleri geçersizdir.");
        if (request.Rooms is < 1 or > 8 || request.Adults is < 1 or > 20 || request.Children is < 0 or > 8 || request.Adults < request.Rooms) return BadRequest("Oda veya misafir sayıları geçersizdir.");
        if (request.ChildAges is null || request.ChildAges.Length != request.Children || request.ChildAges.Any(age => age is < 0 or > 17)) return BadRequest("Çocuk sayısı ile çocuk yaşları eşleşmelidir.");

        await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Postgres"));
        await connection.OpenAsync(cancellationToken);
        var hotel = await HotelAvailabilityService.GetByIdAsync(connection, request.HotelId, request.CheckIn, request.CheckOut, request.Rooms, request.Adults + request.Children, cancellationToken);
        if (hotel is null) return SelectionUnavailable("Otel satışa kapanmış veya seçilen tarihlerde artık yeterli odası kalmamış.");
        var option = hotel.Options.SingleOrDefault(candidate => candidate.Key == request.OptionKey);
        if (option is null) return SelectionUnavailable("Seçilen oda dağılımı artık müsait değil. Güncel sonuçlardan yeni bir seçenek belirleyin.");

        var nights = request.CheckOut.DayNumber - request.CheckIn.DayNumber;
        var priceChanged = request.QuotedTotal is > 0 && request.QuotedTotal != option.TotalPrice;
        return Results.Ok(new
        {
            kind = "hotel",
            title = hotel.Name,
            current = true,
            confirmationRequired = true,
            priceChanged,
            quotedTotal = request.QuotedTotal,
            totalPrice = option.TotalPrice,
            currency = "TRY",
            searchUrl = $"/hotels/results?q={Uri.EscapeDataString(hotel.City)}&checkIn={request.CheckIn:yyyy-MM-dd}&checkOut={request.CheckOut:yyyy-MM-dd}&rooms={request.Rooms}&adults={request.Adults}&children={request.Children}&childAges={string.Join(',', request.ChildAges)}",
            stay = new { hotel.Id, hotel.Name, hotel.City, hotel.District, hotel.Stars, request.CheckIn, request.CheckOut, nights, request.Rooms, request.Adults, request.Children, option },
            priceBreakdown = option.Rooms.Select(room => new { room.RoomId, room.Name, room.Quantity, nightlyTotal = room.NightlyTotal, lineTotal = room.LineTotal, nights = room.Nights.Select(night => new { night.Date, night.Price }) }),
            conditions = new { cancellation = hotel.CancellationPolicy, board = hotel.BoardTypes }
        });
    }

    private static async Task<IResult> BuildFlightSummaryAsync(FlightBookingSummaryRequest request, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var origin = (request.From ?? "").Trim().ToUpperInvariant();
        var destination = (request.To ?? "").Trim().ToUpperInvariant();
        var tripType = (request.TripType ?? "").Trim().ToLowerInvariant();
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (request.OutboundFareId == Guid.Empty || origin.Length != 3 || destination.Length != 3 || origin == destination) return BadRequest("Uçuş seçimi veya rota geçersizdir.");
        if (request.DepartureDate < today || tripType is not ("one-way" or "round-trip")) return BadRequest("Uçuş tarihi veya yolculuk türü geçersizdir.");
        if (tripType == "round-trip" && (request.InboundFareId is null || request.ReturnDate is null || request.ReturnDate < request.DepartureDate)) return BadRequest("Gidiş dönüş uçuşunda dönüş seçimi ve tarihi geçerli olmalıdır.");
        if (tripType == "one-way" && (request.InboundFareId is not null || request.ReturnDate is not null)) return BadRequest("Tek yön uçuşunda dönüş seçimi bulunamaz.");
        if (request.Adults is < 1 or > 9 || request.Children is < 0 or > 8 || request.Infants is < 0 or > 9 || request.Infants > request.Adults || request.Adults + request.Children + request.Infants > 20) return BadRequest("Yolcu sayıları geçersizdir.");

        var seatedPassengers = request.Adults + request.Children;
        var travelerCount = seatedPassengers + request.Infants;
        await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Postgres"));
        await connection.OpenAsync(cancellationToken);
        var outboundOptions = await FlightSearchService.SearchAsync(connection, origin, destination, request.DepartureDate, seatedPassengers, cancellationToken);
        var outbound = outboundOptions.SingleOrDefault(option => option.Fare.Id == request.OutboundFareId);
        if (outbound is null) return SelectionUnavailable("Seçilen gidiş bileti satışa kapanmış veya yeterli koltuğu kalmamış.");

        FlightItinerary? inbound = null;
        if (tripType == "round-trip")
        {
            var inboundOptions = await FlightSearchService.SearchAsync(connection, destination, origin, request.ReturnDate!.Value, seatedPassengers, cancellationToken);
            inbound = inboundOptions.SingleOrDefault(option => option.Fare.Id == request.InboundFareId);
            if (inbound is null) return SelectionUnavailable("Seçilen dönüş bileti satışa kapanmış veya yeterli koltuğu kalmamış.");
            if (inbound.Fare.Currency != outbound.Fare.Currency) return SelectionUnavailable("Gidiş ve dönüş biletlerinin para birimleri artık eşleşmiyor.");
        }

        var perTraveler = outbound.Fare.Price + (inbound?.Fare.Price ?? 0);
        var totalPrice = perTraveler * travelerCount;
        var priceChanged = request.QuotedTotal is > 0 && request.QuotedTotal != totalPrice;
        return Results.Ok(new
        {
            kind = "flight",
            title = $"{origin} → {destination}",
            current = true,
            confirmationRequired = true,
            priceChanged,
            quotedTotal = request.QuotedTotal,
            totalPrice,
            currency = outbound.Fare.Currency,
            searchUrl = $"/flights?from={origin}&to={destination}&date={request.DepartureDate:yyyy-MM-dd}&tripType={tripType}&adults={request.Adults}&children={request.Children}&infants={request.Infants}" + (request.ReturnDate is null ? "" : $"&returnDate={request.ReturnDate:yyyy-MM-dd}"),
            passengers = new { request.Adults, request.Children, request.Infants, travelerCount, seatedPassengers },
            journey = new { outbound, inbound },
            priceBreakdown = new { outboundPerTraveler = outbound.Fare.Price, inboundPerTraveler = inbound?.Fare.Price, perTraveler, travelerCount, calculation = $"{perTraveler:0.00} × {travelerCount}" },
            conditions = new
            {
                baggage = new[] { $"Gidiş: {outbound.Fare.Baggage}" }.Concat(inbound is null ? Array.Empty<string>() : new[] { $"Dönüş: {inbound.Fare.Baggage}" }),
                changeAndCancellation = new[] { $"Gidiş: {outbound.Fare.ChangePolicy}" }.Concat(inbound is null ? Array.Empty<string>() : new[] { $"Dönüş: {inbound.Fare.ChangePolicy}" })
            }
        });
    }

    private static IResult BadRequest(string message) => Results.BadRequest(new { code = "invalid_summary_request", message });
    private static IResult SelectionUnavailable(string message) => Results.Conflict(new { code = "selection_unavailable", message });
}
