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

internal sealed record BookingTravelerRequest(
    string Type,
    string FirstName,
    string LastName,
    int? Age,
    int? AccompanyingAdultIndex);

internal sealed record BookingContactRequest(
    int AdultIndex,
    string Email,
    string Phone);

internal sealed record HotelBookingDetailsRequest(
    HotelBookingSummaryRequest Selection,
    BookingTravelerRequest[] Travelers,
    BookingContactRequest Contact);

internal sealed record FlightBookingDetailsRequest(
    FlightBookingSummaryRequest Selection,
    BookingTravelerRequest[] Travelers,
    BookingContactRequest Contact);
