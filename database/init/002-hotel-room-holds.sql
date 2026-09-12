CREATE TABLE IF NOT EXISTS hotel_room_holds (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    room_id uuid NOT NULL REFERENCES hotel_rooms(id) ON DELETE CASCADE,
    check_in date NOT NULL,
    check_out date NOT NULL,
    quantity integer NOT NULL CHECK (quantity > 0),
    status text NOT NULL DEFAULT 'held' CHECK (status IN ('held', 'confirmed', 'released', 'expired')),
    held_until timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    CHECK (check_out > check_in)
);

CREATE INDEX IF NOT EXISTS ix_hotel_room_holds_availability
    ON hotel_room_holds (room_id, check_in, check_out)
    WHERE status IN ('held', 'confirmed');

INSERT INTO hotel_room_holds (id, room_id, check_in, check_out, quantity, status, held_until)
SELECT '00000000-0000-0000-0000-000000000101', id, current_date + 3, current_date + 8, 1, 'held', now() + interval '2 hours'
FROM hotel_rooms
WHERE name = 'Ekonomik Oda'
ORDER BY id
LIMIT 1
ON CONFLICT (id) DO NOTHING;
