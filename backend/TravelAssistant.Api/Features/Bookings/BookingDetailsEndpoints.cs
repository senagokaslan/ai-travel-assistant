using System.Text.RegularExpressions;
using Npgsql;
using TravelAssistant.Api.Features.Flights;
using TravelAssistant.Api.Features.Hotels;

namespace TravelAssistant.Api.Features.Bookings;

internal static partial class BookingDetailsEndpoints
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
        if (selection.QuotedTotal is > 0 && selection.QuotedTotal != option.TotalPrice) return Unavailable("Otel fiyatı değişti. Güncel özeti yeniden kontrol edin.");

        return ValidatePeople(request.Travelers, request.Contact, selection.Adults, selection.Children, 0, selection.ChildAges, "hotel");
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
        if (selection.QuotedTotal is > 0 && selection.QuotedTotal != currentTotal) return Unavailable("Uçuş fiyatı değişti. Güncel özeti yeniden kontrol edin.");

        return ValidatePeople(request.Travelers, request.Contact, selection.Adults, selection.Children, selection.Infants, null, "flight");
    }

    private static IResult ValidatePeople(BookingTravelerRequest[]? travelers, BookingContactRequest? contact, int adults, int children, int infants, int[]? expectedChildAges, string kind)
    {
        travelers ??= Array.Empty<BookingTravelerRequest>();
        if (adults is < 1 or > 20 || children is < 0 or > 8 || infants is < 0 or > 9 || infants > adults || travelers.Length != adults + children + infants)
            return Invalid("travelers", "Girilen kişi sayısı aramadaki kişi sayısıyla uyuşmuyor.");

        var normalized = new List<object>(travelers.Length);
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < travelers.Length; index++)
        {
            var traveler = travelers[index];
            if (traveler is null) return Invalid($"travelers.{index}", "Kişi bilgileri eksik.");
            var expectedType = index < adults ? "adult" : index < adults + children ? "child" : "infant";
            if (!string.Equals(traveler.Type, expectedType, StringComparison.Ordinal)) return Invalid($"travelers.{index}.type", "Kişi türü aramadaki dağılımla uyuşmuyor.");

            var firstName = NormalizeName(traveler.FirstName);
            var lastName = NormalizeName(traveler.LastName);
            if (!ValidName().IsMatch(firstName)) return Invalid($"travelers.{index}.firstName", "Ad 2–50 karakter olmalı ve yalnızca harf içermelidir.");
            if (!ValidName().IsMatch(lastName)) return Invalid($"travelers.{index}.lastName", "Soyad 2–50 karakter olmalı ve yalnızca harf içermelidir.");

            int? age = null;
            if (expectedType == "child")
            {
                age = kind == "hotel" && expectedChildAges is not null ? expectedChildAges[index - adults] : traveler.Age;
                if (kind == "hotel" && age is < 0 or > 17 || kind == "flight" && age is < 2 or > 11) return Invalid($"travelers.{index}.age", kind == "flight" ? "Çocuk yolcu yaşı 2–11 arasında olmalıdır." : "Çocuk misafir yaşı 0–17 arasında olmalıdır.");
            }
            else if (expectedType == "infant")
            {
                age = traveler.Age;
                if (age is < 0 or > 1) return Invalid($"travelers.{index}.age", "Bebek yolcu yaşı 0–1 arasında olmalıdır.");
            }

            var identity = $"{firstName}|{lastName}|{age?.ToString() ?? "adult"}";
            if (!identities.Add(identity)) return Invalid($"travelers.{index}.firstName", "Aynı kişi birden fazla kez eklenemez.");
            normalized.Add(new { type = expectedType, firstName, lastName, age, accompanyingAdultIndex = expectedType == "infant" ? traveler.AccompanyingAdultIndex : null });
        }

        var infantLinks = new HashSet<int>();
        for (var index = adults + children; index < travelers.Length; index++)
        {
            var adultIndex = travelers[index].AccompanyingAdultIndex;
            if (adultIndex is null || adultIndex < 0 || adultIndex >= adults) return Invalid($"travelers.{index}.accompanyingAdultIndex", "Her bebek bir yetişkin yolcuyla eşleştirilmelidir.");
            if (!infantLinks.Add(adultIndex.Value)) return Invalid($"travelers.{index}.accompanyingAdultIndex", "Bir yetişkin yalnızca bir bebek yolcuya eşlik edebilir.");
        }

        if (contact is null || contact.AdultIndex < 0 || contact.AdultIndex >= adults) return Invalid("contact.adultIndex", "İletişim kişisi yetişkinlerden biri olmalıdır.");
        var email = (contact.Email ?? "").Trim().ToLowerInvariant();
        if (!ValidEmail().IsMatch(email) || email.Length > 254) return Invalid("contact.email", "Geçerli bir e-posta adresi yazın.");
        var phone = NormalizePhone(contact.Phone);
        if (phone is null) return Invalid("contact.phone", "Telefon numarası ülke koduyla birlikte 10–15 rakam içermelidir.");

        var contactTraveler = travelers[contact.AdultIndex];
        return Results.Ok(new
        {
            validated = true,
            reservationCreated = false,
            travelers = normalized,
            contact = new { adultIndex = contact.AdultIndex, name = $"{NormalizeName(contactTraveler.FirstName)} {NormalizeName(contactTraveler.LastName)}", email, phone },
            message = "Misafir ve iletişim bilgileri rezervasyon özetine eklendi. Henüz rezervasyon oluşturulmadı."
        });
    }

    private static string NormalizeName(string? value) => string.Join(' ', (value ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string? NormalizePhone(string? value)
    {
        var raw = (value ?? "").Trim();
        if (!AllowedPhoneCharacters().IsMatch(raw)) return null;
        var digits = DigitsOnly().Replace(raw, "");
        return digits.Length is >= 10 and <= 15 ? $"+{digits}" : null;
    }

    private static IResult Invalid(string field, string message) => Results.BadRequest(new { code = "invalid_booking_details", field, message });
    private static IResult Unavailable(string message) => Results.Conflict(new { code = "selection_unavailable", message });

    [GeneratedRegex(@"^[\p{L}][\p{L}\p{M} '\-]{0,48}[\p{L}\p{M}]$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidName();

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidEmail();

    [GeneratedRegex(@"^[+\d\s().-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex AllowedPhoneCharacters();

    [GeneratedRegex(@"\D", RegexOptions.CultureInvariant)]
    private static partial Regex DigitsOnly();
}
