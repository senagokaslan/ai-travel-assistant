-- Eğitim amaçlı zengin demo kataloğu. Fiyatlar, stoklar ve müsaitlik gerçek sağlayıcı verisi değildir.
-- Bu dosya yeniden çalıştırılabilir: doğal anahtarlar üzerinden upsert yapar ve kullanıcı/rezervasyon tablolarına dokunmaz.

INSERT INTO travel_cities (country_id, name)
SELECT country.id, city.name
FROM travel_countries country
CROSS JOIN (VALUES ('Bursa'), ('Konya'), ('Nevşehir')) AS city(name)
WHERE country.code = 'TR'
ON CONFLICT (country_id, name) DO NOTHING;

INSERT INTO airports (city_id, name, iata_code, is_active)
SELECT city.id, airport.name, airport.code, true
FROM (VALUES
    ('Bursa', 'Bursa Yenişehir Havalimanı', 'YEI'),
    ('Konya', 'Konya Havalimanı', 'KYA'),
    ('Nevşehir', 'Nevşehir Kapadokya Havalimanı', 'NAV')
) AS airport(city_name, name, code)
JOIN travel_cities city ON city.name = airport.city_name
ON CONFLICT (iata_code) DO UPDATE
SET city_id = EXCLUDED.city_id, name = EXCLUDED.name, is_active = true;

INSERT INTO hotel_features (name)
VALUES ('Spa'), ('Deniz manzarası'), ('Havaalanı servisi'), ('Aile dostu'), ('Çalışma alanı')
ON CONFLICT (name) DO NOTHING;

INSERT INTO hotels (name, city_id, district, stars, rating, description, board_types, is_active)
SELECT hotel.name, city.id, hotel.district, hotel.stars, hotel.rating,
       hotel.description, hotel.board_types, true
FROM (VALUES
    ('Boğaz Panorama Otel', 'İstanbul', 'Beşiktaş', 5::smallint, 4.9::numeric, 'Yüksek fiyat ve premium oda senaryoları için eğitim amaçlı tesis.', ARRAY['breakfast','half']::text[]),
    ('Kadıköy Şehir Oteli', 'İstanbul', 'Kadıköy', 3::smallint, 4.2::numeric, 'Ekonomik ve iş seyahati senaryoları için yerel demo tesisi.', ARRAY['room','breakfast']::text[]),
    ('Lara Aile Resort', 'Antalya', 'Lara', 5::smallint, 4.7::numeric, 'Aile odaları ve düşük stok senaryoları için demo resort.', ARRAY['breakfast','half','all']::text[]),
    ('Çankaya İş Oteli', 'Ankara', 'Çankaya', 4::smallint, 4.3::numeric, 'Kısa iş seyahatleri için eğitim amaçlı şehir oteli.', ARRAY['room','breakfast']::text[]),
    ('Alaçatı Rüzgar Oteli', 'İzmir', 'Çeşme', 4::smallint, 4.6::numeric, 'Hafta sonu fiyat farklarını test etmek için butik otel.', ARRAY['breakfast','half']::text[]),
    ('Meydan Park Otel', 'Bursa', 'Osmangazi', 3::smallint, 4.0::numeric, 'Ekonomik şehir konaklaması için demo tesis.', ARRAY['room','breakfast']::text[]),
    ('Kapadokya Taş Konak', 'Nevşehir', 'Göreme', 4::smallint, 4.8::numeric, 'Süit ve son oda senaryoları için eğitim amaçlı konak.', ARRAY['breakfast','half']::text[]),
    ('Konya Sessiz Otel', 'Konya', 'Selçuklu', 3::smallint, 3.9::numeric, 'Belirli tarihte sonuç bulunamaması senaryosu için demo tesis.', ARRAY['room','breakfast']::text[])
) AS hotel(name, city_name, district, stars, rating, description, board_types)
JOIN travel_cities city ON city.name = hotel.city_name
ON CONFLICT (city_id, name) DO UPDATE
SET district = EXCLUDED.district, stars = EXCLUDED.stars, rating = EXCLUDED.rating,
    description = EXCLUDED.description, board_types = EXCLUDED.board_types, is_active = true;

INSERT INTO hotel_rooms (hotel_id, name, capacity, features, is_active)
SELECT hotel.id, room.name, room.capacity, room.features, true
FROM (VALUES
    ('Boğaz Panorama Otel', 'Deluxe Boğaz', 2::smallint, ARRAY['Wi-Fi','Kahvaltı','Deniz manzarası','Spa']::text[]),
    ('Boğaz Panorama Otel', 'Executive Süit', 3::smallint, ARRAY['Wi-Fi','Kahvaltı','Deniz manzarası','Çalışma alanı']::text[]),
    ('Kadıköy Şehir Oteli', 'Ekonomik Tek Kişilik', 1::smallint, ARRAY['Wi-Fi','Çalışma alanı']::text[]),
    ('Kadıköy Şehir Oteli', 'Standart Çift Kişilik', 2::smallint, ARRAY['Wi-Fi','Kahvaltı']::text[]),
    ('Lara Aile Resort', 'Aile Odası', 4::smallint, ARRAY['Wi-Fi','Havuz','Aile dostu']::text[]),
    ('Lara Aile Resort', 'Bahçe Süiti', 3::smallint, ARRAY['Wi-Fi','Havuz','Kahvaltı']::text[]),
    ('Çankaya İş Oteli', 'Business Oda', 2::smallint, ARRAY['Wi-Fi','Kahvaltı','Çalışma alanı','Otopark']::text[]),
    ('Alaçatı Rüzgar Oteli', 'Avlu Odası', 2::smallint, ARRAY['Wi-Fi','Kahvaltı']::text[]),
    ('Alaçatı Rüzgar Oteli', 'Taş Süit', 3::smallint, ARRAY['Wi-Fi','Kahvaltı','Havuz']::text[]),
    ('Meydan Park Otel', 'Standart Oda', 2::smallint, ARRAY['Wi-Fi','Otopark']::text[]),
    ('Kapadokya Taş Konak', 'Mağara Oda', 2::smallint, ARRAY['Wi-Fi','Kahvaltı','Havaalanı servisi']::text[]),
    ('Kapadokya Taş Konak', 'Teras Süit', 4::smallint, ARRAY['Wi-Fi','Kahvaltı','Aile dostu']::text[]),
    ('Konya Sessiz Otel', 'Standart Oda', 2::smallint, ARRAY['Wi-Fi','Kahvaltı']::text[])
) AS room(hotel_name, name, capacity, features)
JOIN hotels hotel ON hotel.name = room.hotel_name
ON CONFLICT (hotel_id, name) DO UPDATE
SET capacity = EXCLUDED.capacity, features = EXCLUDED.features, is_active = true;

INSERT INTO room_daily_rates (room_id, stay_date, nightly_price, rooms_available)
SELECT room.id, day.stay_date,
       CASE
           WHEN hotel.name = 'Boğaz Panorama Otel' AND day.stay_date BETWEEN current_date + 21 AND current_date + 23 THEN 18500
           WHEN hotel.name = 'Boğaz Panorama Otel' THEN 9800
           WHEN hotel.name = 'Lara Aile Resort' THEN CASE WHEN EXTRACT(ISODOW FROM day.stay_date) IN (6,7) THEN 7600 ELSE 6200 END
           WHEN hotel.name = 'Kapadokya Taş Konak' THEN 5400
           WHEN room.name LIKE '%Süit%' THEN 4900
           WHEN hotel.name IN ('Kadıköy Şehir Oteli', 'Meydan Park Otel', 'Konya Sessiz Otel') THEN 2100
           ELSE 3400
       END::numeric,
       CASE
           WHEN hotel.name = 'Konya Sessiz Otel' AND day.stay_date = current_date + 45 THEN 0
           WHEN hotel.name = 'Lara Aile Resort' AND day.stay_date BETWEEN current_date + 14 AND current_date + 16 THEN 1
           WHEN hotel.name = 'Kapadokya Taş Konak' AND day.stay_date BETWEEN current_date + 30 AND current_date + 31 THEN 2
           ELSE 5
       END
FROM hotels hotel
JOIN hotel_rooms room ON room.hotel_id = hotel.id
CROSS JOIN LATERAL (
    SELECT generated::date AS stay_date
    FROM generate_series(current_date + 1, current_date + 365, interval '1 day') generated
) day
WHERE hotel.name IN ('Boğaz Panorama Otel','Kadıköy Şehir Oteli','Lara Aile Resort','Çankaya İş Oteli','Alaçatı Rüzgar Oteli','Meydan Park Otel','Kapadokya Taş Konak','Konya Sessiz Otel')
ON CONFLICT (room_id, stay_date) DO UPDATE SET nightly_price = EXCLUDED.nightly_price;

-- Kapadokya'da +30/+32 aralığında iki baz odadan biri onaylı hold altında: API tam olarak son bir odayı gösterir.
INSERT INTO hotel_room_holds (id, room_id, check_in, check_out, quantity, status, held_until)
SELECT '00000000-0000-0000-0000-000000000830', room.id, current_date + 30, current_date + 32, 1, 'confirmed', NULL
FROM hotel_rooms room
JOIN hotels hotel ON hotel.id = room.hotel_id
WHERE hotel.name = 'Kapadokya Taş Konak' AND room.name = 'Mağara Oda'
ON CONFLICT (id) DO UPDATE
SET room_id = EXCLUDED.room_id, check_in = EXCLUDED.check_in, check_out = EXCLUDED.check_out,
    quantity = EXCLUDED.quantity, status = EXCLUDED.status, held_until = EXCLUDED.held_until;

INSERT INTO flights (flight_number, airline_id, status)
SELECT flight.number, airline.id, flight.status
FROM (VALUES
    ('TK8101','TK','scheduled'), ('PC8102','PC','scheduled'), ('XQ8103','XQ','scheduled'), ('TK8199','TK','cancelled'),
    ('TK8201','TK','scheduled'), ('PC8202','PC','scheduled'),
    ('PC8301','PC','scheduled'), ('TK8302','TK','scheduled'),
    ('VF8401','VF','scheduled'), ('TK8402','TK','scheduled'),
    ('VF8501','VF','scheduled')
) AS flight(number, airline_code, status)
JOIN airlines airline ON BTRIM(airline.iata_code) = flight.airline_code
ON CONFLICT (flight_number) DO UPDATE SET airline_id = EXCLUDED.airline_id, status = EXCLUDED.status;

INSERT INTO flight_legs (flight_id, leg_order, departure_airport_id, arrival_airport_id, departure_at, arrival_at, seats_available)
SELECT flight.id, leg.leg_order, departure.id, arrival.id,
       current_date + leg.departure_offset, current_date + leg.arrival_offset, leg.seats
FROM (VALUES
    ('TK8101',1::smallint,'IST','AYT',interval '14 days 8 hours',interval '14 days 9 hours 20 minutes',12),
    ('PC8102',1::smallint,'SAW','AYT',interval '14 days 10 hours',interval '14 days 11 hours 15 minutes',3),
    ('XQ8103',1::smallint,'IST','AYT',interval '14 days 18 hours',interval '14 days 19 hours 20 minutes',1),
    ('TK8199',1::smallint,'IST','AYT',interval '14 days 12 hours',interval '14 days 13 hours 20 minutes',20),
    ('TK8201',1::smallint,'AYT','IST',interval '18 days 9 hours',interval '18 days 10 hours 20 minutes',10),
    ('PC8202',1::smallint,'AYT','SAW',interval '18 days 16 hours',interval '18 days 17 hours 15 minutes',2),
    ('PC8301',1::smallint,'IST','ADB',interval '21 days 7 hours',interval '21 days 8 hours 10 minutes',1),
    ('TK8302',1::smallint,'IST','ADB',interval '21 days 15 hours',interval '21 days 16 hours 10 minutes',8),
    ('VF8401',1::smallint,'IST','ESB',interval '30 days 6 hours',interval '30 days 7 hours 5 minutes',6),
    ('VF8401',2::smallint,'ESB','TZX',interval '30 days 8 hours 10 minutes',interval '30 days 9 hours 25 minutes',4),
    ('TK8402',1::smallint,'IST','TZX',interval '30 days 11 hours',interval '30 days 12 hours 40 minutes',5),
    ('VF8501',1::smallint,'ESB','ADB',interval '35 days 13 hours',interval '35 days 14 hours 20 minutes',7)
) AS leg(flight_number, leg_order, departure_code, arrival_code, departure_offset, arrival_offset, seats)
JOIN flights flight ON flight.flight_number = leg.flight_number
JOIN airports departure ON BTRIM(departure.iata_code) = leg.departure_code
JOIN airports arrival ON BTRIM(arrival.iata_code) = leg.arrival_code
ON CONFLICT (flight_id, leg_order) DO UPDATE
SET departure_airport_id = EXCLUDED.departure_airport_id, arrival_airport_id = EXCLUDED.arrival_airport_id,
    departure_at = EXCLUDED.departure_at, arrival_at = EXCLUDED.arrival_at,
    seats_available = EXCLUDED.seats_available;

INSERT INTO flight_fares (flight_id, name, price, currency, baggage, change_policy, seats_available, is_active)
SELECT flight.id, fare.name, fare.price, fare.currency, fare.baggage, fare.change_policy, fare.seats, fare.active
FROM (VALUES
    ('TK8101','Ekonomi Demo',2350::numeric,'TRY','Kabin + 15 kg','Değişiklik ücretli',12,true),
    ('TK8101','Business Demo',14900::numeric,'TRY','Kabin + 30 kg','Esnek değişiklik',4,true),
    ('PC8102','Avantaj',1750::numeric,'TRY','El bagajı 8 kg','İade ve değişiklik yok',3,true),
    ('XQ8103','Son Koltuk',1990::numeric,'TRY','El bagajı 8 kg','Değişiklik ücretli',1,true),
    ('XQ8103','Tükendi',1600::numeric,'TRY','El bagajı 8 kg','İade ve değişiklik yok',0,true),
    ('TK8199','İptal Sefer',900::numeric,'TRY','El bagajı 8 kg','Sefer iptal',20,true),
    ('TK8201','Ekonomi Demo',2450::numeric,'TRY','Kabin + 15 kg','Değişiklik ücretli',10,true),
    ('PC8202','Son İki Koltuk',1850::numeric,'TRY','El bagajı 8 kg','İade ve değişiklik yok',2,true),
    ('PC8301','Son Koltuk',2200::numeric,'TRY','El bagajı 8 kg','Değişiklik ücretli',1,true),
    ('TK8302','Yüksek Fiyat Flex',12800::numeric,'TRY','Kabin + 25 kg','Ücretsiz değişiklik',8,true),
    ('VF8401','Aktarmalı Eco',2850::numeric,'TRY','Kabin bagajı 8 kg','Değişiklik ücretli',4,true),
    ('TK8402','Direkt Flex',5100::numeric,'TRY','Kabin + 20 kg','Bir kez ücretsiz değişiklik',5,true),
    ('VF8501','EcoFly Demo',1650::numeric,'TRY','Kabin bagajı 8 kg','Değişiklik ücretli',7,true)
) AS fare(flight_number, name, price, currency, baggage, change_policy, seats, active)
JOIN flights flight ON flight.flight_number = fare.flight_number
ON CONFLICT (flight_id, name) DO UPDATE
SET price = EXCLUDED.price, currency = EXCLUDED.currency, baggage = EXCLUDED.baggage,
    change_policy = EXCLUDED.change_policy, is_active = EXCLUDED.is_active;
