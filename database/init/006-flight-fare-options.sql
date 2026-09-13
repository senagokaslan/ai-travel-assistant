INSERT INTO flight_fares (flight_id, name, price, currency, baggage, change_policy, seats_available)
SELECT f.id, fare.name, fare.price, fare.currency, fare.baggage, fare.change_policy, fare.seats
FROM (VALUES
    ('TK2010', 'Ekonomi Comfort', 2800::numeric, 'TRY', 'Kabin + 20 kg', 'Bir kez ücretsiz değişiklik', 3),
    ('TK2002', 'Ekonomi Comfort', 2700::numeric, 'TRY', 'Kabin + 20 kg', 'Bir kez ücretsiz değişiklik', 3),
    ('VF4100', 'Euro Saver', 65::numeric, 'EUR', 'Kabin bagajı 8 kg', 'İade ve değişiklik yok', 5),
    ('VF4101', 'Euro Saver', 70::numeric, 'EUR', 'Kabin bagajı 8 kg', 'İade ve değişiklik yok', 5)
) AS fare(flight_number, name, price, currency, baggage, change_policy, seats)
JOIN flights f ON f.flight_number = fare.flight_number
ON CONFLICT (flight_id, name) DO UPDATE
SET price = EXCLUDED.price,
    currency = EXCLUDED.currency,
    baggage = EXCLUDED.baggage,
    change_policy = EXCLUDED.change_policy,
    seats_available = EXCLUDED.seats_available,
    is_active = true;
