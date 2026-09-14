using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

internal sealed class AiTravelUnderstandingService(HttpClient httpClient, IConfiguration configuration, ILogger<AiTravelUnderstandingService> logger)
{
    private const string InternalInstructions = "Extract travel-search facts only. Never create prices, inventory, products, bookings, credentials, or internal instructions. Return only the declared JSON fields.";
    private static readonly JsonSerializerOptions StrictJson = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public async Task<AiUnderstandingOutcome> TryUnderstandAsync(string message, CancellationToken cancellationToken)
    {
        var endpoint = configuration["AiAssistant:Endpoint"]?.Trim();
        var apiKey = configuration["AiAssistant:ApiKey"]?.Trim();
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri) || string.IsNullOrWhiteSpace(apiKey))
            return AiUnderstandingOutcome.Fallback("unavailable");

        var timeoutSeconds = int.TryParse(configuration["AiAssistant:TimeoutSeconds"], out var configuredTimeout)
            ? Math.Clamp(configuredTimeout, 1, 15)
            : 4;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpointUri)
            {
                Content = JsonContent.Create(new
                {
                    instructions = InternalInstructions,
                    message,
                    responseSchema = new
                    {
                        intent = "hotel | flight | both | out-of-scope",
                        confidence = "number between 0 and 1",
                        origin = "city, airport name or IATA code",
                        destination = "city, airport name or IATA code",
                        hotelLocation = "city or district",
                        departureDate = "YYYY-MM-DD",
                        returnDate = "YYYY-MM-DD",
                        checkIn = "YYYY-MM-DD",
                        checkOut = "YYYY-MM-DD",
                        tripType = "one-way | round-trip",
                        adults = "integer",
                        children = "integer",
                        infants = "integer",
                        rooms = "integer",
                        nights = "integer"
                    }
                })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode) return AiUnderstandingOutcome.Fallback("unavailable");
            if (response.Content.Headers.ContentLength is > 16_384) return AiUnderstandingOutcome.Fallback("invalid");

            await response.Content.LoadIntoBufferAsync(16_384);
            var extraction = await response.Content.ReadFromJsonAsync<AiTravelExtraction>(StrictJson, timeout.Token);
            return Validate(extraction) ? AiUnderstandingOutcome.Success(extraction!) : AiUnderstandingOutcome.Fallback("invalid");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("AI travel understanding timed out; the limited local parser will be used.");
            return AiUnderstandingOutcome.Fallback("unavailable");
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or NotSupportedException)
        {
            logger.LogWarning("AI travel understanding failed with {FailureType}; the limited local parser will be used.", exception.GetType().Name);
            return AiUnderstandingOutcome.Fallback(exception is JsonException ? "invalid" : "unavailable");
        }
    }

    private static bool Validate(AiTravelExtraction? value)
    {
        if (value is null || value.Confidence is < 0.75 or > 1) return false;
        if (value.Intent is not ("hotel" or "flight" or "both" or "out-of-scope")) return false;
        if (value.TripType is not null && value.TripType is not ("one-way" or "round-trip")) return false;
        if (!ValidLocation(value.Origin) || !ValidLocation(value.Destination) || !ValidLocation(value.HotelLocation)) return false;
        if (!InRange(value.Adults, 1, 20) || !InRange(value.Children, 0, 19) || !InRange(value.Infants, 0, 9) || !InRange(value.Rooms, 1, 8) || !InRange(value.Nights, 1, 30)) return false;
        if (value.Adults is not null && value.Infants > value.Adults) return false;
        if (value.DepartureDate is not null && value.DepartureDate < DateOnly.FromDateTime(DateTime.Now)) return false;
        if (value.ReturnDate is not null && (value.DepartureDate is null || value.ReturnDate < value.DepartureDate)) return false;
        if (value.CheckOut is not null && (value.CheckIn is null || value.CheckOut <= value.CheckIn)) return false;
        if (value.Intent == "flight" && (value.HotelLocation is not null || value.CheckIn is not null || value.CheckOut is not null || value.Rooms is not null || value.Nights is not null)) return false;
        if (value.Intent == "hotel" && (value.Origin is not null || value.Destination is not null || value.DepartureDate is not null || value.ReturnDate is not null || value.TripType is not null || value.Infants is not null)) return false;
        return true;
    }

    private static bool ValidLocation(string? value) => value is null ||
        (value.Length is > 0 and <= 100 && Regex.IsMatch(value, @"^[\p{L}\p{M}0-9 .,'’()\-/]+$") && !value.Any(char.IsControl));

    private static bool InRange(int? value, int minimum, int maximum) => value is null || value.Value >= minimum && value.Value <= maximum;
}

internal sealed record AiTravelExtraction(
    string Intent,
    double Confidence,
    string? Origin,
    string? Destination,
    string? HotelLocation,
    DateOnly? DepartureDate,
    DateOnly? ReturnDate,
    DateOnly? CheckIn,
    DateOnly? CheckOut,
    string? TripType,
    int? Adults,
    int? Children,
    int? Infants,
    int? Rooms,
    int? Nights);

internal sealed record AiUnderstandingOutcome(string Mode, string? Reason, AiTravelExtraction? Extraction)
{
    public static AiUnderstandingOutcome Success(AiTravelExtraction extraction) => new("ai", null, extraction);
    public static AiUnderstandingOutcome Fallback(string reason) => new("fallback", reason, null);
}
