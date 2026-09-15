export type Airport = { code: string; name: string; city: string; country: string }

type FlightFare = {
  id: string
  name: string
  price: number
  currency: string
  baggage: string
  changePolicy: string
}

type FlightSegment = {
  id: string
  order: number
  from: string
  fromAirport: string
  to: string
  toAirport: string
  departureAt: string
  arrivalAt: string
  durationMinutes: number
  seatsAvailable: number
}

export type FlightItinerary = {
  id: string
  flightNumber: string
  airline: string
  airlineCode: string
  departureAt: string
  arrivalAt: string
  durationMinutes: number
  stops: number
  seatsAvailable: number
  fare: FlightFare
  segments: FlightSegment[]
}

export type FlightJourney = {
  id: string
  tripType: 'one-way' | 'round-trip'
  pricePerTraveler: number
  totalPrice: number
  travelerCount: number
  currency: string
  seatsAvailable: number
  outbound: FlightItinerary
  inbound: FlightItinerary | null
}
