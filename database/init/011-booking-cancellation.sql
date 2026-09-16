ALTER TABLE app_bookings
ADD COLUMN IF NOT EXISTS cancelled_at timestamptz;

CREATE INDEX IF NOT EXISTS ix_app_bookings_user_created
    ON app_bookings (user_id, created_at DESC);

CREATE TABLE IF NOT EXISTS flight_booking_leg_allocations (
    booking_id uuid NOT NULL REFERENCES app_bookings(id) ON DELETE CASCADE,
    leg_id uuid NOT NULL REFERENCES flight_legs(id),
    seats integer NOT NULL CHECK (seats > 0),
    PRIMARY KEY (booking_id, leg_id)
);

INSERT INTO flight_booking_leg_allocations (booking_id, leg_id, seats)
SELECT allocation.booking_id, leg.id, allocation.seats
FROM flight_booking_allocations allocation
JOIN flight_fares fare ON fare.id = allocation.fare_id
JOIN flight_legs leg ON leg.flight_id = fare.flight_id
ON CONFLICT (booking_id, leg_id) DO NOTHING;
