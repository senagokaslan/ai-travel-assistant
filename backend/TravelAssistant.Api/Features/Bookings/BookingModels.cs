namespace TravelAssistant.Api.Features.Bookings;

internal sealed record HotelBookingSummaryRequest(
    Guid HotelId,
    string OptionKey,
    DateOnly CheckIn,
    DateOnly CheckOut,
    int Rooms,
    int Adults,
    int Children,
    int[] ChildAges,
    decimal? QuotedTotal);

internal sealed record FlightBookingSummaryRequest(
    Guid OutboundFareId,
    Guid? InboundFareId,
    string From,
    string To,
    DateOnly DepartureDate,
    DateOnly? ReturnDate,
    string TripType,
    int Adults,
    int Children,
    int Infants,
    decimal? QuotedTotal);
