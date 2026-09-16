using Npgsql;
using TravelAssistant.Api.Features.Flights;
using TravelAssistant.Api.Features.Hotels;

namespace TravelAssistant.Api.Features.Bookings;

internal static class BookingDetailsEndpoints
{
    public static IEndpointRouteBuilder MapBookingDetailsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/bookings/details/hotel/validate", ValidateHotelDetailsAsync);
        app.MapPost("/api/bookings/details/flight/validate", ValidateFlightDetailsAsync);
        return app;
    }

    private static async Task<IResult> ValidateHotelDetailsAsync(HotelBookingDetailsRequest request, IConfiguration configuration, CancellationToken cancellationToken)
    {
        if (request.Selection is null) return Invalid("selection", "Rezervasyon seçimi eksik.");
        var selection = request.Selection;
        if (selection.HotelId == Guid.Empty || string.IsNullOrWhiteSpace(selection.OptionKey) || selection.OptionKey.Length > 1_000) return Invalid("selection", "Otel seçimi geçersiz.");
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (selection.CheckIn < today || selection.CheckOut <= selection.CheckIn || selection.CheckOut.DayNumber - selection.CheckIn.DayNumber > 30) return Invalid("selection", "Konaklama tarihleri geçersiz.");
        if (selection.Rooms is < 1 or > 8 || selection.Adults is < 1 or > 20 || selection.Children is < 0 or > 8 || selection.Adults < selection.Rooms) return Invalid("selection", "Oda veya misafir sayıları geçersiz.");
        if (selection.ChildAges is null || selection.ChildAges.Length != selection.Children || selection.ChildAges.Any(age => age is < 0 or > 17)) return Invalid("selection", "Çocuk sayısı ile çocuk yaşları eşleşmiyor.");

        await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Postgres"));
        await connection.OpenAsync(cancellationToken);
        var hotel = await HotelAvailabilityService.GetByIdAsync(connection, selection.HotelId, selection.CheckIn, selection.CheckOut, selection.Rooms, selection.Adults + selection.Children, cancellationToken);
        var option = hotel?.Options.SingleOrDefault(candidate => candidate.Key == selection.OptionKey);
        if (option is null) return Unavailable("Otel veya oda seçeneği artık müsait değil.");
        if (selection.QuotedTotal is > 0 && (selection.QuotedTotal != option.TotalPrice || !string.Equals(selection.QuotedCurrency, "TRY", StringComparison.OrdinalIgnoreCase))) return Unavailable("Otel fiyatı değişti. Güncel özeti yeniden kontrol edin.");

        return PeopleResult(BookingPeopleValidator.Validate(request.Travelers, request.Contact, selection.Adults, selection.Children, 0, selection.ChildAges, "hotel"));
    }

    private static async Task<IResult> ValidateFlightDetailsAsync(FlightBookingDetailsRequest request, IConfiguration configuration, CancellationToken cancellationToken)
    {
        if (request.Selection is null) return Invalid("selection", "Rezervasyon seçimi eksik.");
        var selection = request.Selection;
        var origin = (selection.From ?? "").Trim().ToUpperInvariant();
        var destination = (selection.To ?? "").Trim().ToUpperInvariant();
        var tripType = (selection.TripType ?? "").Trim().ToLowerInvariant();
        var seatedPassengers = selection.Adults + selection.Children;
        if (selection.OutboundFareId == Guid.Empty || origin.Length != 3 || destination.Length != 3 || origin == destination) return Invalid("selection", "Uçuş seçimi geçersiz.");
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (selection.DepartureDate < today || tripType is not ("one-way" or "round-trip")) return Invalid("selection", "Uçuş tarihi veya yolculuk türü geçersiz.");
        if (selection.Adults is < 1 or > 9 || selection.Children is < 0 or > 8 || selection.Infants is < 0 or > 9 || selection.Infants > selection.Adults || selection.Adults + selection.Children + selection.Infants > 20) return Invalid("selection", "Yolcu sayıları geçersiz.");
        if (tripType == "round-trip" && (selection.InboundFareId is null || selection.ReturnDate is null || selection.ReturnDate < selection.DepartureDate)) return Invalid("selection", "Dönüş uçuşu seçimi geçersiz.");
        if (tripType == "one-way" && (selection.InboundFareId is not null || selection.ReturnDate is not null)) return Invalid("selection", "Tek yön uçuşunda dönüş seçimi bulunamaz.");

        await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Postgres"));
        await connection.OpenAsync(cancellationToken);
        var outboundOptions = await FlightSearchService.SearchAsync(connection, origin, destination, selection.DepartureDate, seatedPassengers, cancellationToken);
        var outbound = outboundOptions.SingleOrDefault(option => option.Fare.Id == selection.OutboundFareId);
        if (outbound is null) return Unavailable("Gidiş uçuşu artık satışta değil veya yeterli koltuğu kalmadı.");
        FlightItinerary? inbound = null;
        if (tripType == "round-trip")
        {
            if (selection.InboundFareId is null || selection.ReturnDate is null) return Invalid("selection", "Dönüş uçuşu seçimi eksik.");
            var inboundOptions = await FlightSearchService.SearchAsync(connection, destination, origin, selection.ReturnDate.Value, seatedPassengers, cancellationToken);
            inbound = inboundOptions.SingleOrDefault(option => option.Fare.Id == selection.InboundFareId);
            if (inbound is null) return Unavailable("Dönüş uçuşu artık satışta değil veya yeterli koltuğu kalmadı.");
            if (inbound.Fare.Currency != outbound.Fare.Currency) return Unavailable("Gidiş ve dönüş biletlerinin para birimleri değişti. Güncel özeti yeniden kontrol edin.");
        }
        var currentTotal = (outbound.Fare.Price + (inbound?.Fare.Price ?? 0)) * (selection.Adults + selection.Children + selection.Infants);
        if (selection.QuotedTotal is > 0 && (selection.QuotedTotal != currentTotal || !string.Equals(selection.QuotedCurrency, outbound.Fare.Currency, StringComparison.OrdinalIgnoreCase))) return Unavailable("Uçuş fiyatı değişti. Güncel özeti yeniden kontrol edin.");

        return PeopleResult(BookingPeopleValidator.Validate(request.Travelers, request.Contact, selection.Adults, selection.Children, selection.Infants, null, "flight"));
    }

    private static IResult PeopleResult(BookingPeopleValidation validation)
    {
        if (!validation.IsValid) return Invalid(validation.Field!, validation.Error!);
        return Results.Ok(new
        {
            validated = true,
            reservationCreated = false,
            validation.Value!.Travelers,
            validation.Value.Contact,
            message = "Misafir ve iletişim bilgileri rezervasyon özetine eklendi. Henüz rezervasyon oluşturulmadı."
        });
    }

    private static IResult Invalid(string field, string message) => Results.BadRequest(new { code = "invalid_booking_details", field, message });
    private static IResult Unavailable(string message) => Results.Conflict(new { code = "selection_unavailable", message });

}
