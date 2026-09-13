ALTER TABLE flight_legs
ADD COLUMN IF NOT EXISTS seats_available integer NOT NULL DEFAULT 20 CHECK (seats_available >= 0);

INSERT INTO flights (flight_number, airline_id, status)
SELECT values.flight_number, airlines.id, values.status
FROM (VALUES
    ('TK2002', 'TK', 'scheduled'),
    ('TK2010', 'TK', 'scheduled'),
    ('VF4100', 'VF', 'scheduled'),
    ('VF4101', 'VF', 'scheduled'),
    ('PC3999', 'PC', 'scheduled'),
    ('XQ902', 'XQ', 'scheduled'),
    ('TK2099', 'TK', 'cancelled')
) AS values(flight_number, airline_code, status)
JOIN airlines ON airlines.iata_code = values.airline_code
ON CONFLICT (flight_number) DO UPDATE SET status = EXCLUDED.status;

INSERT INTO flight_legs (flight_id, leg_order, departure_airport_id, arrival_airport_id, departure_at, arrival_at, seats_available)
SELECT f.id, leg.leg_order, dep.id, arr.id,
       current_date + leg.departure_offset, current_date + leg.arrival_offset, leg.seats
FROM (VALUES
    ('TK2002', 1, 'AYT', 'IST', interval '5 days 12 hours', interval '5 days 13 hours 20 minutes', 9),
    ('TK2010', 1, 'IST', 'AYT', interval '1 day 23 hours 30 minutes', interval '2 days 45 minutes', 4),
    ('VF4100', 1, 'IST', 'ESB', interval '1 day 6 hours 30 minutes', interval '1 day 7 hours 35 minutes', 6),
    ('VF4100', 2, 'ESB', 'AYT', interval '1 day 8 hours 50 minutes', interval '1 day 10 hours', 5),
    ('VF4101', 1, 'AYT', 'ESB', interval '5 days 15 hours', interval '5 days 16 hours 10 minutes', 7),
    ('VF4101', 2, 'ESB', 'IST', interval '5 days 17 hours 20 minutes', interval '5 days 18 hours 25 minutes', 7),
    ('PC3999', 1, 'IST', 'ESB', interval '1 day 11 hours', interval '1 day 12 hours', 10),
    ('PC3999', 2, 'ESB', 'AYT', interval '1 day 12 hours 20 minutes', interval '1 day 13 hours 25 minutes', 10),
    ('XQ902', 1, 'IST', 'AYT', interval '1 day 16 hours', interval '1 day 17 hours 20 minutes', 1),
    ('TK2099', 1, 'IST', 'AYT', interval '1 day 18 hours', interval '1 day 19 hours 15 minutes', 20)
) AS leg(flight_number, leg_order, departure_code, arrival_code, departure_offset, arrival_offset, seats)
JOIN flights f ON f.flight_number = leg.flight_number
JOIN airports dep ON dep.iata_code = leg.departure_code
JOIN airports arr ON arr.iata_code = leg.arrival_code
ON CONFLICT (flight_id, leg_order) DO UPDATE
SET departure_airport_id = EXCLUDED.departure_airport_id,
    arrival_airport_id = EXCLUDED.arrival_airport_id,
    departure_at = EXCLUDED.departure_at,
    arrival_at = EXCLUDED.arrival_at,
    seats_available = EXCLUDED.seats_available;

INSERT INTO flight_fares (flight_id, name, price, baggage, change_policy, seats_available)
SELECT f.id, fare.name, fare.price, fare.baggage, fare.change_policy, fare.seats
FROM (VALUES
    ('TK2002', 'Ekonomi Basic', 1950::numeric, 'El bagajı 8 kg', 'Değişiklik ücretli', 9),
    ('TK2010', 'Ekonomi Basic', 2050::numeric, 'El bagajı 8 kg', 'Değişiklik ücretli', 4),
    ('VF4100', 'EcoFly', 1650::numeric, 'Kabin bagajı 8 kg', 'Değişiklik ücretli', 8),
    ('VF4101', 'EcoFly', 1750::numeric, 'Kabin bagajı 8 kg', 'Değişiklik ücretli', 7),
    ('PC3999', 'Standart', 1350::numeric, 'El bagajı 8 kg', 'İade ve değişiklik yok', 10),
    ('XQ902', 'Eco', 1250::numeric, 'El bagajı 8 kg', 'Değişiklik ücretli', 10),
    ('TK2099', 'Ekonomi Basic', 1100::numeric, 'El bagajı 8 kg', 'Değişiklik ücretli', 20)
) AS fare(flight_number, name, price, baggage, change_policy, seats)
JOIN flights f ON f.flight_number = fare.flight_number
ON CONFLICT (flight_id, name) DO UPDATE
SET price = EXCLUDED.price,
    baggage = EXCLUDED.baggage,
    change_policy = EXCLUDED.change_policy,
    seats_available = EXCLUDED.seats_available,
    is_active = true;
