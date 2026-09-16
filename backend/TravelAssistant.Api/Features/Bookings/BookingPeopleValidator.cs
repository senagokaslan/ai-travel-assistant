using System.Text.RegularExpressions;

namespace TravelAssistant.Api.Features.Bookings;

internal sealed record ValidatedBookingTraveler(string Type, string FirstName, string LastName, int? Age, int? AccompanyingAdultIndex);
internal sealed record ValidatedBookingContact(int AdultIndex, string Name, string Email, string Phone);
internal sealed record ValidatedBookingPeople(IReadOnlyList<ValidatedBookingTraveler> Travelers, ValidatedBookingContact Contact);
internal sealed record BookingPeopleValidation(ValidatedBookingPeople? Value, string? Field, string? Error)
{
    public bool IsValid => Value is not null;
}

internal static partial class BookingPeopleValidator
{
    public static BookingPeopleValidation Validate(BookingTravelerRequest[]? travelers, BookingContactRequest? contact, int adults, int children, int infants, int[]? expectedChildAges, string kind)
    {
        travelers ??= Array.Empty<BookingTravelerRequest>();
        if (adults is < 1 or > 20 || children is < 0 or > 8 || infants is < 0 or > 9 || infants > adults || travelers.Length != adults + children + infants)
            return Invalid("travelers", "Girilen kişi sayısı aramadaki kişi sayısıyla uyuşmuyor.");

        var normalized = new List<ValidatedBookingTraveler>(travelers.Length);
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
            normalized.Add(new ValidatedBookingTraveler(expectedType, firstName, lastName, age, expectedType == "infant" ? traveler.AccompanyingAdultIndex : null));
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

        var contactTraveler = normalized[contact.AdultIndex];
        return new BookingPeopleValidation(
            new ValidatedBookingPeople(normalized, new ValidatedBookingContact(contact.AdultIndex, $"{contactTraveler.FirstName} {contactTraveler.LastName}", email, phone)),
            null,
            null);
    }

    private static BookingPeopleValidation Invalid(string field, string message) => new(null, field, message);
    private static string NormalizeName(string? value) => string.Join(' ', (value ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string? NormalizePhone(string? value)
    {
        var raw = (value ?? "").Trim();
        if (!AllowedPhoneCharacters().IsMatch(raw)) return null;
        var digits = DigitsOnly().Replace(raw, "");
        return digits.Length is >= 10 and <= 15 ? $"+{digits}" : null;
    }

    [GeneratedRegex(@"^[\p{L}][\p{L}\p{M} '\-]{0,48}[\p{L}\p{M}]$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidName();

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidEmail();

    [GeneratedRegex(@"^[+\d\s().-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex AllowedPhoneCharacters();

    [GeneratedRegex(@"\D", RegexOptions.CultureInvariant)]
    private static partial Regex DigitsOnly();
}
