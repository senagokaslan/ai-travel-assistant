WITH ranked_seed_hotels AS (
    SELECT id,
           row_number() OVER (PARTITION BY city_id, name ORDER BY xmin::text::bigint) AS occurrence
    FROM hotels
    WHERE name IN (
        'Galata Meydan Otel',
        'Kalepark Konaklama',
        'Kordon Butik Otel',
        'Uzungöl Dağ Evi'
    )
)
DELETE FROM hotels hotel
USING ranked_seed_hotels ranked
WHERE hotel.id = ranked.id
  AND ranked.occurrence > 1;

CREATE UNIQUE INDEX IF NOT EXISTS ux_hotels_city_name
    ON hotels (city_id, name);

INSERT INTO hotel_room_holds (id, room_id, check_in, check_out, quantity, status, held_until)
SELECT '00000000-0000-0000-0000-000000000101', id, current_date + 3, current_date + 8, 1, 'held', now() + interval '2 hours'
FROM hotel_rooms
WHERE name = 'Ekonomik Oda'
ORDER BY id
LIMIT 1
ON CONFLICT (id) DO NOTHING;
