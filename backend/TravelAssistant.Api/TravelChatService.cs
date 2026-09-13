using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;

internal static partial class TravelChatService
{
    private static readonly Dictionary<string, int> TurkishNumbers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bir"] = 1, ["iki"] = 2, ["uc"] = 3, ["dort"] = 4, ["bes"] = 5,
        ["alti"] = 6, ["yedi"] = 7, ["sekiz"] = 8, ["dokuz"] = 9, ["on"] = 10
    };

    public static async Task<ChatReply> ReplyAsync(
        NpgsqlConnection connection,
        Guid conversationId,
        string latestMessage,
        CancellationToken cancellationToken)
    {
        var context = await LoadContextAsync(connection, conversationId, cancellationToken);
        var normalized = Normalize(latestMessage);
        UpdateIntent(context, normalized);
        UpdateDates(context, latestMessage, normalized);
        UpdateCounts(context, normalized);

        if (context.Intent == "flight")
        {
            await UpdateFlightLocationsAsync(connection, context, normalized, cancellationToken);
        }
        else if (context.Intent == "hotel")
        {
            await UpdateHotelLocationAsync(connection, context, normalized, cancellationToken);
        }

        var reply = context.Intent switch
        {
            "flight" => await BuildFlightReplyAsync(connection, context, cancellationToken),
            "hotel" => await BuildHotelReplyAsync(connection, context, cancellationToken),
            _ => BuildPromptReply(context, "Otel mi yoksa uçuş mu aramak istediğini yazar mısın? Örneğin “Antalya’da 14–16 Eylül için otel” diyebilirsin.", "Seyahat türü")
        };

        var contextJson = JsonSerializer.Serialize(context, JsonOptions);
        await using var update = new NpgsqlCommand(
            "UPDATE chat_conversations SET context=@context::jsonb, updated_at=now() WHERE id=@id",
            connection);
        update.Parameters.AddWithValue("context", contextJson);
        update.Parameters.AddWithValue("id", conversationId);
        await update.ExecuteNonQueryAsync(cancellationToken);
        return reply with { ContextJson = contextJson };
    }

    private static async Task<ChatContext> LoadContextAsync(NpgsqlConnection connection, Guid conversationId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT context::text FROM chat_conversations WHERE id=@id", connection);
        command.Parameters.AddWithValue("id", conversationId);
        var json = (string?)await command.ExecuteScalarAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(json) ? new ChatContext() : JsonSerializer.Deserialize<ChatContext>(json, JsonOptions) ?? new ChatContext();
    }

    private static void UpdateIntent(ChatContext context, string text)
    {
        if (Regex.IsMatch(text, @"\b(ucus|ucak|ucmak|havaalani|havalimani)\b")) context.Intent = "flight";
        if (Regex.IsMatch(text, @"\b(otel|konaklama|konaklamak|oda)\b")) context.Intent = "hotel";
    }

    private static void UpdateDates(ChatContext context, string original, string normalized)
    {
        var dates = ExtractDates(original).ToArray();
        if (normalized.Contains("yarin")) dates = dates.Append(DateOnly.FromDateTime(DateTime.Now).AddDays(1)).Distinct().ToArray();
        if (normalized.Contains("obur gun") || normalized.Contains("ertesi gun")) dates = dates.Append(DateOnly.FromDateTime(DateTime.Now).AddDays(2)).Distinct().ToArray();
        if (dates.Length == 0) return;

        if (context.Intent == "hotel")
        {
            context.CheckIn ??= dates[0];
            if (dates.Length > 1) context.CheckOut = dates[1];
            else if (context.CheckIn != dates[0]) context.CheckOut ??= dates[0];
        }
        else
        {
            context.DepartureDate ??= dates[0];
            if (dates.Length > 1) { context.ReturnDate = dates[1]; context.TripType = "round-trip"; }
            else if (context.DepartureDate != dates[0]) { context.ReturnDate ??= dates[0]; context.TripType = "round-trip"; }
        }
        if (normalized.Contains("gidis donus") || normalized.Contains("donuslu")) context.TripType = "round-trip";
        if (normalized.Contains("tek yon")) { context.TripType = "one-way"; context.ReturnDate = null; }
    }

    private static void UpdateCounts(ChatContext context, string text)
    {
        foreach (Match match in CountRegex().Matches(text))
        {
            var value = int.TryParse(match.Groups[1].Value, out var parsed) ? parsed : TurkishNumbers.GetValueOrDefault(match.Groups[1].Value);
            switch (match.Groups[2].Value)
            {
                case "yetiskin": context.Adults = value; break;
                case "cocuk": context.Children = value; break;
                case "bebek": context.Infants = value; break;
                case "oda": context.Rooms = value; break;
                case "kisi": context.Adults = value; break;
            }
        }
    }

    private static async Task UpdateFlightLocationsAsync(NpgsqlConnection connection, ChatContext context, string text, CancellationToken cancellationToken)
    {
        var airports = new List<AirportRow>();
        await using (var command = new NpgsqlCommand("SELECT BTRIM(a.iata_code), a.name, c.name FROM airports a JOIN travel_cities c ON c.id=a.city_id WHERE a.is_active ORDER BY c.name, a.iata_code", connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken)) airports.Add(new AirportRow(reader.GetString(0), reader.GetString(1), reader.GetString(2)));

        var explicitAirports = airports
            .Select(airport =>
            {
                var code = Regex.Match(text, $@"\b{Regex.Escape(Normalize(airport.Code))}\b");
                var nameIndex = text.IndexOf(Normalize(airport.Name), StringComparison.Ordinal);
                return new { Airport = airport, Index = code.Success ? code.Index : nameIndex };
            })
            .Where(match => match.Index >= 0)
            .OrderBy(match => match.Index)
            .Select(match => match.Airport)
            .DistinctBy(airport => airport.Code)
            .ToArray();
        foreach (var airport in explicitAirports) AssignAirport(context, airport.Code);

        if (context.Origin is not null && context.Destination is not null) return;
        var explicitCities = explicitAirports.Select(airport => airport.City).ToHashSet();
        var cityMentions = airports.Select(item => item.City).Distinct()
            .Select(city => new { City = city, Index = text.IndexOf(Normalize(city), StringComparison.Ordinal) })
            .Where(item => item.Index >= 0 && !explicitCities.Contains(item.City)).OrderBy(item => item.Index).ToArray();
        foreach (var mention in cityMentions)
        {
            var options = airports.Where(item => item.City == mention.City).ToArray();
            if (options.Length == 1 && context.PendingField == "origin" && context.Origin is null && context.Destination is null) context.Destination = options[0].Code;
            else if (options.Length == 1) AssignAirport(context, options[0].Code);
            else if (context.Origin is null) { context.PendingField = "origin"; context.PendingCity = mention.City; }
            else if (context.Destination is null) { context.PendingField = "destination"; context.PendingCity = mention.City; }
        }
    }

    private static void AssignAirport(ChatContext context, string code)
    {
        if (context.PendingField == "origin") context.Origin = code;
        else if (context.PendingField == "destination") context.Destination = code;
        else if (context.Origin is null) context.Origin = code;
        else if (context.Destination is null && context.Origin != code) context.Destination = code;
        context.PendingField = null;
        context.PendingCity = null;
    }

    private static async Task UpdateHotelLocationAsync(NpgsqlConnection connection, ChatContext context, string text, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT name FROM travel_cities ORDER BY length(name) DESC", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var city = reader.GetString(0);
            if (text.Contains(Normalize(city))) { context.HotelLocation = city; break; }
        }
    }

    private static async Task<ChatReply> BuildFlightReplyAsync(NpgsqlConnection connection, ChatContext context, CancellationToken cancellationToken)
    {
        if (context.PendingCity is not null)
        {
            await using var command = new NpgsqlCommand("SELECT BTRIM(a.iata_code) || ' — ' || a.name FROM airports a JOIN travel_cities c ON c.id=a.city_id WHERE a.is_active AND c.name=@city ORDER BY a.iata_code", connection);
            command.Parameters.AddWithValue("city", context.PendingCity);
            var options = new List<string>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) options.Add(reader.GetString(0));
            return BuildPromptReply(context, $"{context.PendingCity} için hangi havaalanını seçiyorsun: {string.Join(" veya ", options)}?", context.PendingField == "origin" ? "Kalkış havaalanı" : "Varış havaalanı");
        }
        if (context.Origin is null) return BuildPromptReply(context, "Nereden uçmak istiyorsun? Şehir, havaalanı adı veya IATA kodu yazabilirsin.", "Kalkış havaalanı");
        if (context.Destination is null) return BuildPromptReply(context, "Nereye uçmak istiyorsun?", "Varış havaalanı");
        if (context.Origin == context.Destination) { context.Destination = null; return BuildPromptReply(context, "Kalkış ve varış aynı olamaz. Farklı bir varış havaalanı yazar mısın?", "Varış havaalanı"); }
        if (context.DepartureDate is null) return BuildPromptReply(context, "Hangi tarihte uçmak istiyorsun? Tarihi 14.09.2026 gibi yazabilirsin.", "Gidiş tarihi");
        if (context.DepartureDate < DateOnly.FromDateTime(DateTime.Now)) { context.DepartureDate = null; return BuildPromptReply(context, "Geçmiş tarih için arama yapamam. Yeni bir gidiş tarihi yazar mısın?", "Gidiş tarihi"); }
        if (context.TripType == "round-trip" && context.ReturnDate is null) return BuildPromptReply(context, "Dönüş tarihini de yazar mısın?", "Dönüş tarihi");
        if (context.ReturnDate < context.DepartureDate) { context.ReturnDate = null; return BuildPromptReply(context, "Dönüş tarihi gidişten önce olamaz. Yeni dönüş tarihini yazar mısın?", "Dönüş tarihi"); }

        var adults = context.Adults ?? 1; var children = context.Children ?? 0; var infants = context.Infants ?? 0;
        if (infants > adults) return BuildPromptReply(context, "Her bebek için en az bir yetişkin gerekiyor. Yetişkin sayısını günceller misin?", "Yetişkin sayısı");
        var seated = adults + children;
        var outbound = await FlightSearchService.SearchAsync(connection, context.Origin, context.Destination, context.DepartureDate.Value, seated, cancellationToken);
        var results = new List<object>();
        if (context.TripType == "round-trip")
        {
            var inbound = await FlightSearchService.SearchAsync(connection, context.Destination, context.Origin, context.ReturnDate!.Value, seated, cancellationToken);
            results.AddRange(outbound.SelectMany(outboundItem => inbound.Where(inboundItem => inboundItem.Fare.Currency == outboundItem.Fare.Currency).Select(inboundItem => ToFlightResult(outboundItem, inboundItem, adults + children + infants))).OrderBy(item => item.TotalPrice).Take(3));
        }
        else results.AddRange(outbound.Select(item => ToFlightResult(item, null, adults + children + infants)).OrderBy(item => item.TotalPrice).Take(3));

        var query = $"from={context.Origin}&to={context.Destination}&date={context.DepartureDate:yyyy-MM-dd}&tripType={context.TripType}&adults={adults}&children={children}&infants={infants}" + (context.ReturnDate is null ? "" : $"&returnDate={context.ReturnDate:yyyy-MM-dd}");
        var metadata = new { intent = "flight", understood = Understood(context), missing = Array.Empty<string>(), results, searchUrl = $"/flights?{query}" };
        var message = results.Count == 0 ? "Bu bilgilerle uygun uçuş bulamadım. Tarihi veya rotayı değiştirerek tekrar deneyebiliriz." : $"{context.Origin}–{context.Destination} rotasında en uygun {results.Count} seçeneği buldum.";
        return new ChatReply(message, JsonSerializer.Serialize(metadata, JsonOptions), "");
    }

    private static async Task<ChatReply> BuildHotelReplyAsync(NpgsqlConnection connection, ChatContext context, CancellationToken cancellationToken)
    {
        if (context.HotelLocation is null) return BuildPromptReply(context, "Hangi şehirde veya bölgede konaklamak istiyorsun?", "Konum");
        if (context.CheckIn is null) return BuildPromptReply(context, "Otele giriş tarihini yazar mısın?", "Giriş tarihi");
        if (context.CheckOut is null) return BuildPromptReply(context, "Otelden çıkış tarihini de yazar mısın?", "Çıkış tarihi");
        if (context.CheckIn < DateOnly.FromDateTime(DateTime.Now) || context.CheckOut <= context.CheckIn || context.CheckOut.Value.DayNumber - context.CheckIn.Value.DayNumber > 30)
        { context.CheckIn = null; context.CheckOut = null; return BuildPromptReply(context, "Tarih aralığı geçersiz. En fazla 30 gecelik, bugünden sonraki giriş ve çıkış tarihlerini yazar mısın?", "Konaklama tarihleri"); }
        var adults = context.Adults ?? 2; var children = context.Children ?? 0; var rooms = context.Rooms ?? 1;
        if (adults < rooms) return BuildPromptReply(context, "Her oda için en az bir yetişkin gerekiyor. Yetişkin veya oda sayısını günceller misin?", "Misafir bilgileri");
        var hotels = await HotelAvailabilityService.SearchAsync(connection, context.HotelLocation, context.CheckIn.Value, context.CheckOut.Value, rooms, adults + children, cancellationToken);
        var query = $"q={Uri.EscapeDataString(context.HotelLocation)}&checkIn={context.CheckIn:yyyy-MM-dd}&checkOut={context.CheckOut:yyyy-MM-dd}&rooms={rooms}&adults={adults}&children={children}";
        var results = hotels.Take(3).Select(hotel => new { kind = "hotel", hotel.Id, hotel.Name, hotel.City, hotel.District, hotel.Stars, hotel.Rating, hotel.TotalPrice, currency = "TRY", detailUrl = $"/hotels/{hotel.Id}?{query}&option={Uri.EscapeDataString(hotel.Options[0].Key)}&quotedTotal={hotel.TotalPrice.ToString(CultureInfo.InvariantCulture)}" }).ToArray();
        var metadata = new { intent = "hotel", understood = Understood(context), missing = Array.Empty<string>(), results, searchUrl = $"/hotels/results?{query}" };
        var message = results.Length == 0 ? "Bu bilgilerle müsait otel bulamadım. Tarihleri veya konumu değiştirebiliriz." : $"{context.HotelLocation} için en uygun {results.Length} oteli buldum.";
        return new ChatReply(message, JsonSerializer.Serialize(metadata, JsonOptions), "");
    }

    private static FlightChatResult ToFlightResult(FlightItinerary outbound, FlightItinerary? inbound, int travelers) =>
        new("flight", outbound.FlightNumber + (inbound is null ? "" : $" / {inbound.FlightNumber}"), outbound.Airline, outbound.Segments[0].From, outbound.Segments[^1].To, outbound.DepartureAt, outbound.ArrivalAt, outbound.DurationMinutes + (inbound?.DurationMinutes ?? 0), outbound.Stops + (inbound?.Stops ?? 0), outbound.Fare.Baggage + (inbound is null ? "" : $" · Dönüş: {inbound.Fare.Baggage}"), (outbound.Fare.Price + (inbound?.Fare.Price ?? 0)) * travelers, outbound.Fare.Currency);

    private static ChatReply BuildPromptReply(ChatContext context, string content, string missing)
    {
        var metadata = new { intent = context.Intent, understood = Understood(context), missing = new[] { missing }, results = Array.Empty<object>() };
        return new ChatReply(content, JsonSerializer.Serialize(metadata, JsonOptions), "");
    }

    private static Dictionary<string, string> Understood(ChatContext context)
    {
        var values = new Dictionary<string, string>();
        if (context.Intent is not null) values["Arama"] = context.Intent == "flight" ? "Uçuş" : "Otel";
        if (context.Origin is not null) values["Kalkış"] = context.Origin;
        if (context.Destination is not null) values["Varış"] = context.Destination;
        if (context.HotelLocation is not null) values["Konum"] = context.HotelLocation;
        if (context.DepartureDate is not null) values["Gidiş"] = context.DepartureDate.Value.ToString("dd.MM.yyyy");
        if (context.ReturnDate is not null) values["Dönüş"] = context.ReturnDate.Value.ToString("dd.MM.yyyy");
        if (context.CheckIn is not null) values["Giriş"] = context.CheckIn.Value.ToString("dd.MM.yyyy");
        if (context.CheckOut is not null) values["Çıkış"] = context.CheckOut.Value.ToString("dd.MM.yyyy");
        if (context.Adults is not null) values["Yetişkin"] = context.Adults.Value.ToString();
        if (context.Children is > 0) values["Çocuk"] = context.Children.Value.ToString();
        if (context.Infants is > 0) values["Bebek"] = context.Infants.Value.ToString();
        if (context.Rooms is not null) values["Oda"] = context.Rooms.Value.ToString();
        return values;
    }

    private static IEnumerable<DateOnly> ExtractDates(string text)
    {
        foreach (Match match in DateRegex().Matches(text))
        {
            var value = match.Value;
            if (DateOnly.TryParseExact(value, new[] { "dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) yield return date;
        }
    }

    private static string Normalize(string value)
    {
        var decomposed = value.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        foreach (var character in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark) builder.Append(character == 'ı' ? 'i' : character);
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [GeneratedRegex(@"\b(\d{1,2}|bir|iki|uc|dort|bes|alti|yedi|sekiz|dokuz|on)\s*(yetiskin|cocuk|bebek|kisi|oda)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CountRegex();
    [GeneratedRegex(@"\b(?:\d{1,2}[./]\d{1,2}[./]\d{4}|\d{4}-\d{2}-\d{2})\b")]
    private static partial Regex DateRegex();

    private sealed record AirportRow(string Code, string Name, string City);
    private sealed record FlightChatResult(string Kind, string FlightNumber, string Airline, string From, string To, DateTime DepartureAt, DateTime ArrivalAt, int DurationMinutes, int Stops, string Baggage, decimal TotalPrice, string Currency);
}

internal sealed class ChatContext
{
    public string? Intent { get; set; }
    public string TripType { get; set; } = "one-way";
    public string? Origin { get; set; }
    public string? Destination { get; set; }
    public string? PendingField { get; set; }
    public string? PendingCity { get; set; }
    public DateOnly? DepartureDate { get; set; }
    public DateOnly? ReturnDate { get; set; }
    public string? HotelLocation { get; set; }
    public DateOnly? CheckIn { get; set; }
    public DateOnly? CheckOut { get; set; }
    public int? Adults { get; set; }
    public int? Children { get; set; }
    public int? Infants { get; set; }
    public int? Rooms { get; set; }
}

internal sealed record ChatReply(string Content, string MetadataJson, string ContextJson);
