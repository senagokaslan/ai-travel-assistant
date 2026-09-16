ALTER TABLE app_bookings
ADD COLUMN IF NOT EXISTS request_key uuid,
ADD COLUMN IF NOT EXISTS reference_code text,
ADD COLUMN IF NOT EXISTS total_price numeric(12,2),
ADD COLUMN IF NOT EXISTS currency char(3),
ADD COLUMN IF NOT EXISTS details jsonb,
ADD COLUMN IF NOT EXISTS confirmed_at timestamptz;

CREATE UNIQUE INDEX IF NOT EXISTS ux_app_bookings_user_request
    ON app_bookings (user_id, request_key)
    WHERE request_key IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_app_bookings_reference_code
    ON app_bookings (reference_code)
    WHERE reference_code IS NOT NULL;

ALTER TABLE hotel_room_holds
ADD COLUMN IF NOT EXISTS booking_id uuid REFERENCES app_bookings(id) ON DELETE CASCADE;

CREATE INDEX IF NOT EXISTS ix_hotel_room_holds_booking
    ON hotel_room_holds (booking_id)
    WHERE booking_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS flight_booking_allocations (
    booking_id uuid NOT NULL REFERENCES app_bookings(id) ON DELETE CASCADE,
    fare_id uuid NOT NULL REFERENCES flight_fares(id),
    seats integer NOT NULL CHECK (seats > 0),
    PRIMARY KEY (booking_id, fare_id)
);
