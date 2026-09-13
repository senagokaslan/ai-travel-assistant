using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Npgsql;

internal static partial class TravelChatService
{
    private static readonly Dictionary<string, int> TurkishNumbers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bir"] = 1, ["iki"] = 2, ["uc"] = 3, ["dort"] = 4, ["bes"] = 5,
        ["alti"] = 6, ["yedi"] = 7, ["sekiz"] = 8, ["dokuz"] = 9, ["on"] = 10
    };
    private static readonly Dictionary<string, int> TurkishMonths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ocak"] = 1, ["subat"] = 2, ["mart"] = 3, ["nisan"] = 4, ["mayis"] = 5, ["haziran"] = 6,
        ["temmuz"] = 7, ["agustos"] = 8, ["eylul"] = 9, ["ekim"] = 10, ["kasim"] = 11, ["aralik"] = 12
    };
    private static readonly Dictionary<string, DayOfWeek> TurkishWeekdays = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pazartesi"] = DayOfWeek.Monday, ["sali"] = DayOfWeek.Tuesday, ["carsamba"] = DayOfWeek.Wednesday,
        ["persembe"] = DayOfWeek.Thursday, ["cuma"] = DayOfWeek.Friday, ["cumartesi"] = DayOfWeek.Saturday, ["pazar"] = DayOfWeek.Sunday
    };

    public static async Task<ChatReply> ReplyAsync(
        NpgsqlConnection connection,
        Guid conversationId,
        string latestMessage,
        CancellationToken cancellationToken)
    {
        var context = await LoadContextAsync(connection, conversationId, cancellationToken);
        var normalized = Normalize(latestMessage);
        context.AppliedChange = null;
        context.InvalidCommand = null;
        context.IsFilterChange = false;
        context.IsFilterRemoval = false;
        context.PreviousSortPreference = context.SortPreference;
        context.PreviousNonstopOnly = context.NonstopOnly;
        context.PreviousHotelStars = context.HotelStars;
        var routing = Classify(context, normalized);
        context.LastClassification = routing.Classification;
        context.LastConfidence = routing.Confidence;

        ChatReply reply;
        if (routing.Classification == "out-of-scope")
        {
            reply = BuildRoutingReply(context, "Bu konuda işlem yapamıyorum. Otel arayabilir, uçuş bulabilir veya ikisini birlikte planlamana yardımcı olabilirim.", "out-of-scope", "high", "Otel veya uçuş isteği");
            return await SaveAndReturnAsync(connection, conversationId, context, reply, cancellationToken);
        }
        if (routing.Classification == "ambiguous")
        {
            reply = BuildRoutingReply(context, "Otel mi, uçuş mu, yoksa ikisini birden mi istediğini kısaca belirtir misin? Henüz bir arama başlatmadım.", "ambiguous", "low", "Seyahat türü");
            return await SaveAndReturnAsync(connection, conversationId, context, reply, cancellationToken);
        }
        if (routing.Classification == "both")
        {
            ResetContext(context, "both");
            context.LastClassification = "both"; context.LastConfidence = "high";
            reply = BuildRoutingReply(context, "Hem uçuş hem otel istediğini anladım. Bilgileri karıştırmamak için önce hangisiyle başlayalım: uçuş mu, otel mi?", "both", "high", "Öncelik");
            return await SaveAndReturnAsync(connection, conversationId, context, reply, cancellationToken);
        }
        if (routing.Intent is not null && context.Intent != routing.Intent) ResetContext(context, routing.Intent);
        else if (routing.Intent is not null) context.Intent = routing.Intent;

        var hadSearchBase = context.HasSearchBase;
        var previousDates = (context.DepartureDate, context.ReturnDate, context.CheckIn, context.CheckOut);
        var previousPassengers = (context.Adults, context.Children, context.Infants, context.Rooms);
        UpdateResultCriteria(context, normalized);
        UpdateDates(context, latestMessage, normalized);
        UpdateCounts(context, normalized);

        if (hadSearchBase && context.AppliedChange is null && previousDates != (context.DepartureDate, context.ReturnDate, context.CheckIn, context.CheckOut))
            context.AppliedChange = "Tarih bilgisi güncellendi";
        if (hadSearchBase && previousPassengers != (context.Adults, context.Children, context.Infants, context.Rooms))
            context.AppliedChange = context.AppliedChange is null ? "Yolcu bilgisi güncellendi" : $"{context.AppliedChange}; yolcu bilgisi güncellendi";

        if (context.Intent == "flight")
        {
            await UpdateFlightLocationsAsync(connection, context, normalized, cancellationToken);
        }
        else if (context.Intent == "hotel")
        {
            await UpdateHotelLocationAsync(connection, context, normalized, cancellationToken);
        }

        if (context.InvalidCommand is not null)
        {
            reply = BuildPromptReply(context, context.InvalidCommand, "Geçerli filtre");
            return await SaveAndReturnAsync(connection, conversationId, context, reply, cancellationToken);
        }

        reply = context.Intent switch
        {
            "flight" => await BuildFlightReplyAsync(connection, context, cancellationToken),
            "hotel" => await BuildHotelReplyAsync(connection, context, cancellationToken),
            _ => BuildPromptReply(context, "Otel mi yoksa uçuş mu aramak istediğini yazar mısın? Örneğin “Antalya’da 14–16 Eylül için otel” diyebilirsin.", "Seyahat türü")
        };

        return await SaveAndReturnAsync(connection, conversationId, context, reply, cancellationToken);
    }

    private static async Task<ChatReply> SaveAndReturnAsync(NpgsqlConnection connection, Guid conversationId, ChatContext context, ChatReply reply, CancellationToken cancellationToken)
    {
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

    private static RouteDecision Classify(ChatContext context, string text)
    {
        var flight = Regex.IsMatch(text, @"\b(ucus|ucak|ucmak|havaalani|havalimani|bilet|gidis|donus|tek yon)\b") || Regex.IsMatch(text, @"\b[\p{L}]+['’]?(?:dan|den|tan|ten)\b.+\b[\p{L}]+['’]?(?:ya|ye)\b");
        var hotel = Regex.IsMatch(text, @"\b(otel|konaklama|konaklamak|oda|gece|gecelik)\b");
        if (flight && hotel) return new RouteDecision("both", null, "high");
        if (flight) return new RouteDecision("flight", "flight", "high");
        if (hotel) return new RouteDecision("hotel", "hotel", "high");

        if (Regex.IsMatch(text, @"\b(futbol|mac|borsa|hisse|kripto|yemek|tarif|kod|programlama|siyaset|film|muzik|hava durumu)\b"))
            return new RouteDecision("out-of-scope", null, "high");
        if (context.Intent is "flight" or "hotel" && (context.Awaiting is not null || CountRegex().IsMatch(text) || DateRegex().IsMatch(text) || NaturalDateRegex().IsMatch(text) || Regex.IsMatch(text, @"\b(olsun|yerine|degistir|guncelle|duzelt|yarin|haftaya|cikis|giris|ucuz\w*|yildiz|aktarmasiz|direkt|filtre\w*|tumunu|hepsini|ileri|geri)\b")))
            return new RouteDecision("continuation", context.Intent, "medium");
        if (Regex.IsMatch(text, @"\b(seyahat|tatil|gezi|gitmek|plan)\b") || text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 3)
            return new RouteDecision("ambiguous", null, "low");
        return new RouteDecision("out-of-scope", null, "medium");
    }

    private static void ResetContext(ChatContext context, string intent)
    {
        context.Intent = intent; context.TripType = null;
        context.Origin = null; context.Destination = null; context.PendingField = null; context.PendingCity = null;
        context.DepartureDate = null; context.ReturnDate = null; context.HotelLocation = null;
        context.CheckIn = null; context.CheckOut = null; context.Adults = null; context.Children = null;
        context.Infants = null; context.Rooms = null; context.Nights = null; context.DateAmbiguous = false; context.LocationError = null; context.Awaiting = null;
        context.SortPreference = null; context.NonstopOnly = false; context.HotelStars = null; context.HasSearchBase = false;
    }

    private static void UpdateResultCriteria(ChatContext context, string text)
    {
        var allowsStops = Regex.IsMatch(text, @"\b(aktarmali da|aktarma olabilir|direkt olmasin)\b");
        var mentionsNonstop = Regex.IsMatch(text, @"\b(aktarmasiz|direkt)\b");
        var hasNonstop = mentionsNonstop && !allowsStops;
        if (mentionsNonstop && allowsStops)
        {
            context.InvalidCommand = "Aynı anda hem aktarmasız hem aktarmalı seçenek isteyemezsin. Hangisini tercih ettiğini yazar mısın?";
            return;
        }

        if (Regex.IsMatch(text, @"\b(filtreleri? kaldir|filtreyi sifirla|tumunu goster|hepsini goster)\b"))
        {
            context.SortPreference = null; context.NonstopOnly = false; context.HotelStars = null;
            context.IsFilterChange = true; context.IsFilterRemoval = true; context.AppliedChange = "Tüm sonuç filtreleri kaldırıldı";
            return;
        }

        var starMatch = Regex.Match(text, @"\b(\d{1,2}|bir|iki|uc|dort|bes|alti|yedi|sekiz|dokuz|on)\s*yildiz(?:li)?\b");
        if (starMatch.Success)
        {
            var stars = int.TryParse(starMatch.Groups[1].Value, out var parsed) ? parsed : TurkishNumbers.GetValueOrDefault(starMatch.Groups[1].Value);
            if (context.Intent != "hotel") context.InvalidCommand = "Yıldız filtresi yalnızca otel sonuçlarına uygulanabilir.";
            else if (stars is < 1 or > 5) context.InvalidCommand = "Otel yıldız filtresi 1–5 arasında olmalı.";
            else { context.HotelStars = stars; context.IsFilterChange = true; context.AppliedChange = $"Yalnızca {stars} yıldızlı oteller gösteriliyor"; }
            return;
        }

        if (hasNonstop)
        {
            if (context.Intent != "flight") context.InvalidCommand = "Aktarmasız filtresi yalnızca uçuş sonuçlarına uygulanabilir.";
            else { context.NonstopOnly = true; context.IsFilterChange = true; context.AppliedChange = "Yalnızca aktarmasız uçuşlar gösteriliyor"; }
            return;
        }
        if (allowsStops)
        {
            if (context.Intent != "flight") context.InvalidCommand = "Aktarma filtresi yalnızca uçuş sonuçlarına uygulanabilir.";
            else { context.NonstopOnly = false; context.IsFilterChange = true; context.AppliedChange = "Aktarmalı uçuşlar yeniden gösteriliyor"; }
            return;
        }
        if (Regex.IsMatch(text, @"\b(daha ucuz\w*|en ucuz\w*|ucuza gore)\b"))
        {
            context.SortPreference = "price"; context.IsFilterChange = true; context.AppliedChange = "Sonuçlar en ucuzdan sıralandı";
            return;
        }

        var shiftMatch = Regex.Match(text, @"\b(\d{1,2}|bir|iki|uc|dort|bes|alti|yedi|sekiz|dokuz|on)\s*gun\s*(ileri|geri)\b");
        if (shiftMatch.Success)
        {
            var days = int.TryParse(shiftMatch.Groups[1].Value, out var parsed) ? parsed : TurkishNumbers.GetValueOrDefault(shiftMatch.Groups[1].Value);
            if (shiftMatch.Groups[2].Value == "geri") days *= -1;
            if (context.Intent == "flight" && context.DepartureDate is not null)
            {
                context.DepartureDate = context.DepartureDate.Value.AddDays(days);
                if (context.ReturnDate is not null) context.ReturnDate = context.ReturnDate.Value.AddDays(days);
                context.AppliedChange = $"Uçuş tarihleri {Math.Abs(days)} gün {(days > 0 ? "ileri" : "geri")} alındı";
            }
            else if (context.Intent == "hotel" && context.CheckIn is not null)
            {
                context.CheckIn = context.CheckIn.Value.AddDays(days);
                if (context.CheckOut is not null) context.CheckOut = context.CheckOut.Value.AddDays(days);
                context.AppliedChange = $"Konaklama tarihleri {Math.Abs(days)} gün {(days > 0 ? "ileri" : "geri")} alındı";
            }
            else context.InvalidCommand = "Tarihi kaydırabilmem için önce aramadaki tarihi belirtmelisin.";
            return;
        }

        if (Regex.IsMatch(text, @"\b(yalnizca|filtrele|filtre)\b"))
            context.InvalidCommand = context.Intent == "flight"
                ? "Bu uçuş filtresini anlayamadım. ‘Aktarmasız olsun’, ‘daha ucuzlarını göster’ veya ‘filtreleri kaldır’ diyebilirsin."
                : "Bu otel filtresini anlayamadım. ‘Yalnızca dört yıldız’, ‘daha ucuzlarını göster’ veya ‘filtreleri kaldır’ diyebilirsin.";
    }

    private static void UpdateDates(ChatContext context, string original, string normalized)
    {
        if (context.Intent == "flight")
        {
            if (normalized.Contains("gidis donus") || normalized.Contains("donuslu")) context.TripType = "round-trip";
            if (normalized.Contains("tek yon")) { context.TripType = "one-way"; context.ReturnDate = null; }
        }

        var dates = ExtractDates(original).ToArray();
        if (context.Intent == "hotel" && dates.Length == 0 && Regex.IsMatch(normalized, @"\b(haftaya|gelecek hafta|onumuzdeki hafta)\b"))
        {
            context.DateAmbiguous = true;
            return;
        }
        if (normalized.Contains("yarin")) dates = dates.Append(DateOnly.FromDateTime(DateTime.Now).AddDays(1)).Distinct().ToArray();
        if (normalized.Contains("obur gun") || normalized.Contains("ertesi gun")) dates = dates.Append(DateOnly.FromDateTime(DateTime.Now).AddDays(2)).Distinct().ToArray();
        if (context.Intent == "flight" && dates.Length == 0)
        {
            var weekday = TurkishWeekdays.Keys.FirstOrDefault(day => Regex.IsMatch(normalized, $@"\b{day}\b"));
            if (weekday is not null)
            {
                var qualified = Regex.IsMatch(normalized, $@"\b(bu|gelecek|onumuzdeki)\s+{weekday}\b");
                if (!qualified) { context.DateAmbiguous = true; return; }
                var today = DateOnly.FromDateTime(DateTime.Now);
                var offset = ((int)TurkishWeekdays[weekday] - (int)today.DayOfWeek + 7) % 7;
                if (offset == 0) offset = 7;
                if (Regex.IsMatch(normalized, $@"\b(gelecek|onumuzdeki)\s+{weekday}\b")) offset += 7;
                dates = new[] { today.AddDays(offset) };
            }
        }
        if (dates.Length == 0) return;
        context.DateAmbiguous = false;

        if (context.Intent == "hotel")
        {
            var correction = Regex.IsMatch(normalized, @"\b(olsun|yerine|degistir|guncelle|duzelt)\b");
            if (dates.Length > 1)
            {
                context.CheckIn = dates[0]; context.CheckOut = dates[1];
                context.Nights = dates[1].DayNumber - dates[0].DayNumber;
            }
            else if (normalized.Contains("cikis"))
            {
                context.CheckOut = dates[0];
                if (context.CheckIn is not null) context.Nights = dates[0].DayNumber - context.CheckIn.Value.DayNumber;
            }
            else if (context.CheckIn is null || correction || normalized.Contains("giris"))
            {
                context.CheckIn = dates[0];
                if (context.Nights is > 0) context.CheckOut = dates[0].AddDays(context.Nights.Value);
            }
            else if (context.CheckOut is null)
            {
                context.CheckOut = dates[0]; context.Nights = dates[0].DayNumber - context.CheckIn.Value.DayNumber;
            }
            else
            {
                context.CheckIn = dates[0];
                if (context.Nights is > 0) context.CheckOut = dates[0].AddDays(context.Nights.Value);
            }
        }
        else
        {
            var correction = Regex.IsMatch(normalized, @"\b(olsun|yerine|degistir|guncelle|duzelt)\b");
            if (dates.Length > 1) { context.DepartureDate = dates[0]; context.ReturnDate = dates[1]; context.TripType = "round-trip"; }
            else if (context.Awaiting?.Contains("Dönüş", StringComparison.OrdinalIgnoreCase) == true ||
                     (normalized.Contains("donus") && !normalized.Contains("gidis donus")))
            { context.ReturnDate = dates[0]; context.TripType = "round-trip"; }
            else if (context.DepartureDate is null || normalized.Contains("gidis") || correction) context.DepartureDate = dates[0];
            else if (context.TripType == "round-trip" && context.ReturnDate is null) context.ReturnDate = dates[0];
            else context.DepartureDate = dates[0];
        }
    }

    private static void UpdateCounts(ChatContext context, string text)
    {
        foreach (Match match in CountRegex().Matches(text))
        {
            var value = int.TryParse(match.Groups[1].Value, out var parsed) ? parsed : TurkishNumbers.GetValueOrDefault(match.Groups[1].Value);
            ApplyCount(context, value, match.Groups[2].Value);
        }
        foreach (Match match in ReverseCountRegex().Matches(text))
        {
            var value = int.TryParse(match.Groups[2].Value, out var parsed) ? parsed : TurkishNumbers.GetValueOrDefault(match.Groups[2].Value);
            ApplyCount(context, value, match.Groups[1].Value);
        }
    }

    private static void ApplyCount(ChatContext context, int value, string unit)
    {
        if (unit == "yetiskin") context.Adults = value;
        else if (unit == "cocuk") context.Children = value;
        else if (unit == "bebek") context.Infants = value;
        else if (unit == "oda") context.Rooms = value;
        else if (unit.StartsWith("kisi", StringComparison.Ordinal)) context.Adults = value;
        else if (unit.StartsWith("gece", StringComparison.Ordinal))
        {
            context.Nights = value;
            if (context.Intent == "hotel" && context.CheckIn is not null) context.CheckOut = context.CheckIn.Value.AddDays(value);
        }
    }

    private static async Task UpdateFlightLocationsAsync(NpgsqlConnection connection, ChatContext context, string text, CancellationToken cancellationToken)
    {
        var airports = new List<AirportRow>();
        await using (var command = new NpgsqlCommand("SELECT BTRIM(a.iata_code), a.name, c.name FROM airports a JOIN travel_cities c ON c.id=a.city_id WHERE a.is_active ORDER BY c.name, a.iata_code", connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken)) airports.Add(new AirportRow(reader.GetString(0), reader.GetString(1), reader.GetString(2)));

        var cities = new List<string>();
        await using (var cityCommand = new NpgsqlCommand("SELECT name FROM travel_cities ORDER BY length(name) DESC", connection))
        await using (var cityReader = await cityCommand.ExecuteReaderAsync(cancellationToken))
            while (await cityReader.ReadAsync(cancellationToken)) cities.Add(cityReader.GetString(0));
        var cityWithoutAirport = cities.FirstOrDefault(city => text.Contains(Normalize(city)) && airports.All(airport => airport.City != city));
        if (cityWithoutAirport is not null)
        {
            context.LocationError = $"{cityWithoutAirport} için katalogda aktif havaalanı bulunmuyor. Havaalanı olan başka bir şehir seç";
            return;
        }
        context.LocationError = null;

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
        if (explicitAirports.Length >= 2)
        {
            context.Origin = explicitAirports[0].Code; context.Destination = explicitAirports[1].Code;
            context.PendingField = null; context.PendingCity = null;
        }
        else if (explicitAirports.Length == 1)
        {
            var code = explicitAirports[0].Code;
            var isOrigin = Regex.IsMatch(text, @"\b(kalkis|nereden)\b") || Regex.IsMatch(text, $@"\b{Regex.Escape(Normalize(code))}['’]?(?:dan|den|tan|ten)\b");
            var isDestination = Regex.IsMatch(text, @"\b(varis|nereye)\b") || Regex.IsMatch(text, $@"\b{Regex.Escape(Normalize(code))}['’]?(?:ya|ye)\b");
            if (isOrigin) context.Origin = code;
            if (isDestination) context.Destination = code;
            if (!isOrigin && !isDestination) AssignAirport(context, code);
        }

        if (context.Origin is not null && context.Destination is not null && explicitAirports.Length > 0) return;
        var explicitCities = explicitAirports.Select(airport => airport.City).ToHashSet();
        var cityMentions = airports.Select(item => item.City).Distinct()
            .Select(city => new { City = city, Index = text.IndexOf(Normalize(city), StringComparison.Ordinal) })
            .Where(item => item.Index >= 0 && !explicitCities.Contains(item.City)).OrderBy(item => item.Index).ToArray();
        foreach (var mention in cityMentions)
        {
            var options = airports.Where(item => item.City == mention.City).ToArray();
            var cityText = Normalize(mention.City);
            var originCue = Regex.IsMatch(text, $@"\b{Regex.Escape(cityText)}['’]?(?:dan|den|tan|ten)\b") || text.Contains($"kalkis {cityText}") || text.Contains($"nereden {cityText}");
            var destinationCue = Regex.IsMatch(text, $@"\b{Regex.Escape(cityText)}['’]?(?:ya|ye)\b") || text.Contains($"varis {cityText}") || text.Contains($"nereye {cityText}");
            if (options.Length == 1 && originCue) context.Origin = options[0].Code;
            else if (options.Length == 1 && destinationCue) context.Destination = options[0].Code;
            else if (options.Length == 1 && context.PendingField == "origin" && context.Origin is null && context.Destination is null) context.Destination = options[0].Code;
            else if (options.Length == 1) AssignAirport(context, options[0].Code);
            else if (originCue || context.Origin is null) { context.Origin = null; context.PendingField = "origin"; context.PendingCity = mention.City; }
            else if (destinationCue || context.Destination is null) { context.Destination = null; context.PendingField = "destination"; context.PendingCity = mention.City; }
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
        if (context.LocationError is not null)
            return BuildPromptReply(context, context.LocationError, "Havaalanı olan şehir");
        if (context.IsFilterChange && !context.IsFilterRemoval && !context.HasSearchBase)
            return RejectFilterWithoutResults(context, "uçuş");
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
        if (context.DateAmbiguous) return BuildPromptReply(context, "Haftanın gününü anladım ancak tarihi kesinleştirmem gerekiyor. 18.09.2026 veya ‘bu cuma’ gibi yazar mısın?", "Kesin gidiş tarihi");
        if (context.DepartureDate is null) return BuildPromptReply(context, "Hangi tarihte uçmak istiyorsun? Tarihi 14.09.2026 gibi yazabilirsin.", "Gidiş tarihi");
        if (context.DepartureDate < DateOnly.FromDateTime(DateTime.Now)) { context.DepartureDate = null; return BuildPromptReply(context, "Geçmiş tarih için arama yapamam. Yeni bir gidiş tarihi yazar mısın?", "Gidiş tarihi"); }
        if (context.TripType is null) return BuildPromptReply(context, "Uçuş tek yön mü, gidiş dönüş mü?", "Yolculuk yönü");
        if (context.TripType == "round-trip" && context.ReturnDate is null) return BuildPromptReply(context, "Dönüş tarihini de yazar mısın?", "Dönüş tarihi");
        if (context.ReturnDate < context.DepartureDate) { context.ReturnDate = null; return BuildPromptReply(context, "Dönüş tarihi gidişten önce olamaz. Yeni dönüş tarihini yazar mısın?", "Dönüş tarihi"); }

        if (context.Adults is null) return BuildPromptReply(context, "Kaç yetişkin yolcu olacağını yazar mısın? Çocuk ve bebek varsa sayılarını da belirtebilirsin.", "Yetişkin sayısı");
        var adults = context.Adults.Value; var children = context.Children ?? 0; var infants = context.Infants ?? 0;
        if (adults is < 1 or > 9) { context.Adults = null; return BuildPromptReply(context, "Yetişkin sayısı 1–9 arasında olmalı. Güncel sayıyı yazar mısın?", "1–9 arasında yetişkin sayısı"); }
        if (children is < 0 or > 8) { context.Children = null; return BuildPromptReply(context, "Çocuk sayısı 0–8 arasında olmalı. Güncel sayıyı yazar mısın?", "0–8 arasında çocuk sayısı"); }
        if (infants is < 0 or > 9) { context.Infants = null; return BuildPromptReply(context, "Bebek sayısı 0–9 arasında olmalı. Güncel sayıyı yazar mısın?", "0–9 arasında bebek sayısı"); }
        if (infants > adults) return BuildPromptReply(context, "Her bebek için en az bir yetişkin gerekiyor. Yetişkin sayısını günceller misin?", "Yetişkin sayısı");
        if (adults + children + infants > 20) return BuildPromptReply(context, "Toplam yolcu sayısı 20’yi aşamaz. Yolcu sayılarını günceller misin?", "En fazla 20 yolcu");
        var seated = adults + children;
        var outbound = await FlightSearchService.SearchAsync(connection, context.Origin, context.Destination, context.DepartureDate.Value, seated, cancellationToken);
        var allResults = new List<FlightChatResult>();
        if (context.TripType == "round-trip")
        {
            var inbound = await FlightSearchService.SearchAsync(connection, context.Destination, context.Origin, context.ReturnDate!.Value, seated, cancellationToken);
            allResults.AddRange(outbound.SelectMany(outboundItem => inbound.Where(inboundItem => inboundItem.Fare.Currency == outboundItem.Fare.Currency).Select(inboundItem => ToFlightResult(outboundItem, inboundItem, adults + children + infants))));
        }
        else allResults.AddRange(outbound.Select(item => ToFlightResult(item, null, adults + children + infants)));

        context.HasSearchBase = allResults.Count > 0;
        IEnumerable<FlightChatResult> filteredResults = allResults;
        if (context.NonstopOnly) filteredResults = filteredResults.Where(item => item.Stops == 0);
        filteredResults = context.SortPreference == "price"
            ? filteredResults.OrderBy(item => item.TotalPrice).ThenBy(item => item.DurationMinutes)
            : filteredResults.OrderBy(item => item.TotalPrice);
        var results = filteredResults.Take(3).ToArray();

        var query = $"from={context.Origin}&to={context.Destination}&date={context.DepartureDate:yyyy-MM-dd}&tripType={context.TripType}&adults={adults}&children={children}&infants={infants}" + (context.ReturnDate is null ? "" : $"&returnDate={context.ReturnDate:yyyy-MM-dd}");
        context.Awaiting = null;
        var metadata = new { intent = "flight", classification = context.LastClassification, confidence = context.LastConfidence, understood = Understood(context), missing = Array.Empty<string>(), results, searchUrl = $"/flights?{query}", appliedChange = context.AppliedChange };
        var change = context.AppliedChange is null ? "" : $"{context.AppliedChange}. ";
        var message = results.Length == 0
            ? context.HasSearchBase ? $"{change}Bu filtrelerle eşleşen uçuş kalmadı. Filtreyi değiştirebilir veya kaldırabilirsin." : $"{change}Bu bilgilerle uygun uçuş bulamadım. Tarihi veya rotayı değiştirerek tekrar deneyebiliriz."
            : $"{change}{context.Origin}–{context.Destination} rotasında {results.Length} seçenek gösteriyorum.";
        return new ChatReply(message, JsonSerializer.Serialize(metadata, JsonOptions), "");
    }

    private static async Task<ChatReply> BuildHotelReplyAsync(NpgsqlConnection connection, ChatContext context, CancellationToken cancellationToken)
    {
        if (context.IsFilterChange && !context.IsFilterRemoval && !context.HasSearchBase)
            return RejectFilterWithoutResults(context, "otel");
        if (context.CheckIn is not null && context.Nights is > 0) context.CheckOut = context.CheckIn.Value.AddDays(context.Nights.Value);
        var missing = new List<string>();
        if (context.HotelLocation is null) missing.Add("şehir veya bölge");
        if (context.DateAmbiguous) missing.Add("kesin giriş tarihi (ör. 20.07.2027)");
        else if (context.CheckIn is null) missing.Add("giriş tarihi");
        if (context.CheckOut is null && context.Nights is null) missing.Add("çıkış tarihi veya gece sayısı");
        if (context.Adults is null) missing.Add("yetişkin sayısı");
        if (context.Rooms is null) missing.Add("oda sayısı");

        var today = DateOnly.FromDateTime(DateTime.Now);
        if (context.CheckIn is not null && context.CheckIn < today)
        {
            context.CheckIn = null; context.CheckOut = null;
            missing.Remove("giriş tarihi"); missing.Remove("çıkış tarihi veya gece sayısı");
            missing.Add("bugünden sonraki giriş tarihi"); missing.Add("çıkış tarihi veya gece sayısı");
        }
        if (context.Nights is not null && context.Nights is < 1 or > 30)
        {
            context.Nights = null; context.CheckOut = null;
            missing.Remove("çıkış tarihi veya gece sayısı"); missing.Add("1–30 arasında gece sayısı veya geçerli çıkış tarihi");
        }
        if (context.CheckIn is not null && context.CheckOut is not null && (context.CheckOut <= context.CheckIn || context.CheckOut.Value.DayNumber - context.CheckIn.Value.DayNumber > 30))
        {
            context.CheckOut = null; context.Nights = null;
            missing.Remove("çıkış tarihi veya gece sayısı"); missing.Add("girişten sonra ve en fazla 30 gece olacak çıkış tarihi");
        }
        if (context.Adults is not null && context.Adults is < 1 or > 20) { context.Adults = null; missing.Remove("yetişkin sayısı"); missing.Add("1–20 arasında yetişkin sayısı"); }
        if (context.Rooms is not null && context.Rooms is < 1 or > 8) { context.Rooms = null; missing.Remove("oda sayısı"); missing.Add("1–8 arasında oda sayısı"); }
        if (context.Adults is not null && context.Rooms is not null && context.Adults < context.Rooms) missing.Add("her oda için en az bir yetişkin olacak kişi veya oda sayısı");
        missing = missing.Distinct().ToList();
        if (missing.Count > 0)
            return BuildPromptReply(context, $"Otel aramasını başlatmadan önce şu bilgileri tek mesajda yazar mısın: {JoinTurkish(missing)}?", missing);

        var location = context.HotelLocation!; var checkIn = context.CheckIn!.Value; var checkOut = context.CheckOut!.Value;
        var adults = context.Adults!.Value; var children = context.Children ?? 0; var rooms = context.Rooms!.Value;
        var hotels = await HotelAvailabilityService.SearchAsync(connection, location, checkIn, checkOut, rooms, adults + children, cancellationToken);
        var query = $"q={Uri.EscapeDataString(location)}&checkIn={checkIn:yyyy-MM-dd}&checkOut={checkOut:yyyy-MM-dd}&rooms={rooms}&adults={adults}&children={children}";
        context.HasSearchBase = hotels.Count > 0;
        IEnumerable<HotelAvailabilityResult> filteredHotels = hotels;
        if (context.HotelStars is not null) filteredHotels = filteredHotels.Where(hotel => hotel.Stars == context.HotelStars);
        filteredHotels = context.SortPreference == "price" ? filteredHotels.OrderBy(hotel => hotel.TotalPrice).ThenByDescending(hotel => hotel.Rating) : filteredHotels;
        var results = filteredHotels.Take(3).Select(hotel => new { kind = "hotel", hotel.Id, hotel.Name, hotel.City, hotel.District, hotel.Stars, hotel.Rating, hotel.TotalPrice, currency = "TRY", detailUrl = $"/hotels/{hotel.Id}?{query}&option={Uri.EscapeDataString(hotel.Options[0].Key)}&quotedTotal={hotel.TotalPrice.ToString(CultureInfo.InvariantCulture)}" }).ToArray();
        context.Awaiting = null;
        var metadata = new { intent = "hotel", classification = context.LastClassification, confidence = context.LastConfidence, understood = Understood(context), missing = Array.Empty<string>(), results, searchUrl = $"/hotels/results?{query}", appliedChange = context.AppliedChange };
        var change = context.AppliedChange is null ? "" : $"{context.AppliedChange}. ";
        var message = results.Length == 0
            ? context.HasSearchBase ? $"{change}Bu filtrelerle eşleşen otel kalmadı. Filtreyi değiştirebilir veya kaldırabilirsin." : $"{change}Bu bilgilerle müsait otel bulamadım. Tarihleri veya konumu değiştirebiliriz."
            : $"{change}{location} için {results.Length} otel gösteriyorum.";
        return new ChatReply(message, JsonSerializer.Serialize(metadata, JsonOptions), "");
    }

    private static FlightChatResult ToFlightResult(FlightItinerary outbound, FlightItinerary? inbound, int travelers) =>
        new("flight", outbound.FlightNumber + (inbound is null ? "" : $" / {inbound.FlightNumber}"), outbound.Airline, outbound.Segments[0].From, outbound.Segments[^1].To, outbound.DepartureAt, outbound.ArrivalAt, outbound.DurationMinutes + (inbound?.DurationMinutes ?? 0), outbound.Stops + (inbound?.Stops ?? 0), outbound.Fare.Baggage + (inbound is null ? "" : $" · Dönüş: {inbound.Fare.Baggage}"), (outbound.Fare.Price + (inbound?.Fare.Price ?? 0)) * travelers, outbound.Fare.Currency);

    private static ChatReply BuildPromptReply(ChatContext context, string content, string missing)
        => BuildPromptReply(context, content, new[] { missing });

    private static ChatReply RejectFilterWithoutResults(ChatContext context, string resultType)
    {
        context.SortPreference = context.PreviousSortPreference;
        context.NonstopOnly = context.PreviousNonstopOnly;
        context.HotelStars = context.PreviousHotelStars;
        context.AppliedChange = null;
        return BuildPromptReply(context, $"Bu filtreyi uygulayabileceğim mevcut {resultType} sonucu yok. Önce {resultType} aramasını tamamlayalım.", $"Mevcut {resultType} sonucu");
    }

    private static ChatReply BuildPromptReply(ChatContext context, string content, IReadOnlyList<string> missing)
    {
        context.Awaiting = string.Join(", ", missing);
        var metadata = new { intent = context.Intent, classification = context.LastClassification, confidence = context.LastConfidence, understood = Understood(context), missing, results = Array.Empty<object>(), appliedChange = context.AppliedChange };
        return new ChatReply(content, JsonSerializer.Serialize(metadata, JsonOptions), "");
    }

    private static string JoinTurkish(IReadOnlyList<string> values) => values.Count switch
    {
        0 => "",
        1 => values[0],
        2 => $"{values[0]} ve {values[1]}",
        _ => $"{string.Join(", ", values.Take(values.Count - 1))} ve {values[^1]}"
    };

    private static ChatReply BuildRoutingReply(ChatContext context, string content, string classification, string confidence, string missing)
    {
        context.Awaiting = missing;
        var metadata = new { intent = context.Intent, classification, confidence, understood = Understood(context), missing = new[] { missing }, results = Array.Empty<object>() };
        return new ChatReply(content, JsonSerializer.Serialize(metadata, JsonOptions), "");
    }

    private static Dictionary<string, string> Understood(ChatContext context)
    {
        var values = new Dictionary<string, string>();
        if (context.Intent is not null) values["Arama"] = context.Intent switch { "flight" => "Uçuş", "hotel" => "Otel", "both" => "Uçuş + otel", _ => context.Intent };
        if (context.Origin is not null) values["Kalkış"] = context.Origin;
        if (context.Destination is not null) values["Varış"] = context.Destination;
        if (context.TripType is not null) values["Yön"] = context.TripType == "round-trip" ? "Gidiş dönüş" : "Tek yön";
        if (context.HotelLocation is not null) values["Konum"] = context.HotelLocation;
        if (context.DepartureDate is not null) values["Gidiş"] = context.DepartureDate.Value.ToString("dd.MM.yyyy");
        if (context.ReturnDate is not null) values["Dönüş"] = context.ReturnDate.Value.ToString("dd.MM.yyyy");
        if (context.CheckIn is not null) values["Giriş"] = context.CheckIn.Value.ToString("dd.MM.yyyy");
        if (context.CheckOut is not null) values["Çıkış"] = context.CheckOut.Value.ToString("dd.MM.yyyy");
        if (context.Adults is not null) values["Yetişkin"] = context.Adults.Value.ToString();
        if (context.Children is > 0) values["Çocuk"] = context.Children.Value.ToString();
        if (context.Infants is > 0) values["Bebek"] = context.Infants.Value.ToString();
        if (context.Rooms is not null) values["Oda"] = context.Rooms.Value.ToString();
        if (context.Nights is > 0) values["Konaklama"] = $"{context.Nights} gece";
        if (context.NonstopOnly) values["Uçuş filtresi"] = "Aktarmasız";
        if (context.HotelStars is not null) values["Otel filtresi"] = $"{context.HotelStars} yıldız";
        if (context.SortPreference == "price") values["Sıralama"] = "En ucuz";
        return values;
    }

    private static IEnumerable<DateOnly> ExtractDates(string text)
    {
        var found = new HashSet<DateOnly>();
        foreach (Match match in DateRegex().Matches(text))
        {
            var value = match.Value;
            if (DateOnly.TryParseExact(value, new[] { "dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exactDate) && found.Add(exactDate))
            {
                yield return exactDate;
                continue;
            }
            var parts = value.Split('.', '/');
            if (parts.Length == 2 && int.TryParse(parts[0], out var day) && int.TryParse(parts[1], out var month) && TryCreateFutureDate(day, month, null, out var shortDate) && found.Add(shortDate)) yield return shortDate;
        }

        var normalized = Normalize(text);
        foreach (Match match in NaturalDateRegex().Matches(normalized))
        {
            var day = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var month = TurkishMonths[match.Groups[2].Value];
            var year = int.TryParse(match.Groups[3].Value, out var parsedYear) ? parsedYear : (int?)null;
            if (TryCreateFutureDate(day, month, year, out var naturalDate) && found.Add(naturalDate)) yield return naturalDate;
        }
    }

    private static bool TryCreateFutureDate(int day, int month, int? year, out DateOnly date)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var targetYear = year ?? today.Year;
        if (!DateOnly.TryParseExact($"{day:00}.{month:00}.{targetYear}", "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) return false;
        if (year is null && date < today) return DateOnly.TryParseExact($"{day:00}.{month:00}.{targetYear + 1}", "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        return true;
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

    [GeneratedRegex(@"\b(\d{1,2}|bir|iki|uc|dort|bes|alti|yedi|sekiz|dokuz|on)\s*(yetiskin|cocuk|bebek|kisi(?:lik)?|oda|gece(?:lik)?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CountRegex();
    [GeneratedRegex(@"\b(yetiskin|cocuk|bebek|kisi|oda|gece)\s+sayisi\s+(\d{1,2}|bir|iki|uc|dort|bes|alti|yedi|sekiz|dokuz|on)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ReverseCountRegex();
    [GeneratedRegex(@"\b(?:\d{1,2}[./]\d{1,2}(?:[./]\d{4})?|\d{4}-\d{2}-\d{2})\b")]
    private static partial Regex DateRegex();
    [GeneratedRegex(@"\b([0-3]?\d)\s+(ocak|subat|mart|nisan|mayis|haziran|temmuz|agustos|eylul|ekim|kasim|aralik)(?:\s+(\d{4}))?\b")]
    private static partial Regex NaturalDateRegex();

    private sealed record AirportRow(string Code, string Name, string City);
    private sealed record FlightChatResult(string Kind, string FlightNumber, string Airline, string From, string To, DateTime DepartureAt, DateTime ArrivalAt, int DurationMinutes, int Stops, string Baggage, decimal TotalPrice, string Currency);
    private sealed record RouteDecision(string Classification, string? Intent, string Confidence);
}

internal sealed class ChatContext
{
    public string? Intent { get; set; }
    public string? TripType { get; set; }
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
    public int? Nights { get; set; }
    public bool DateAmbiguous { get; set; }
    public string? LocationError { get; set; }
    public string? SortPreference { get; set; }
    public bool NonstopOnly { get; set; }
    public int? HotelStars { get; set; }
    public bool HasSearchBase { get; set; }
    public string? Awaiting { get; set; }
    public string LastClassification { get; set; } = "ambiguous";
    public string LastConfidence { get; set; } = "low";
    [JsonIgnore] public string? AppliedChange { get; set; }
    [JsonIgnore] public string? InvalidCommand { get; set; }
    [JsonIgnore] public bool IsFilterChange { get; set; }
    [JsonIgnore] public bool IsFilterRemoval { get; set; }
    [JsonIgnore] public string? PreviousSortPreference { get; set; }
    [JsonIgnore] public bool PreviousNonstopOnly { get; set; }
    [JsonIgnore] public int? PreviousHotelStars { get; set; }
}

internal sealed record ChatReply(string Content, string MetadataJson, string ContextJson);
