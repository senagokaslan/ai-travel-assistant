CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS travel_countries (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), name text NOT NULL UNIQUE, code char(2) NOT NULL UNIQUE);
CREATE TABLE IF NOT EXISTS travel_cities (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), country_id uuid NOT NULL REFERENCES travel_countries(id), name text NOT NULL, UNIQUE(country_id, name));
CREATE TABLE IF NOT EXISTS airports (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), city_id uuid NOT NULL REFERENCES travel_cities(id), name text NOT NULL, iata_code char(3) NOT NULL UNIQUE, is_active boolean NOT NULL DEFAULT true);
CREATE TABLE IF NOT EXISTS airlines (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), name text NOT NULL UNIQUE, iata_code char(2) NOT NULL UNIQUE, is_active boolean NOT NULL DEFAULT true);
CREATE TABLE IF NOT EXISTS hotels (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), name text NOT NULL, city_id uuid NOT NULL REFERENCES travel_cities(id), district text NOT NULL, stars smallint NOT NULL CHECK (stars BETWEEN 1 AND 5), rating numeric(2,1) NOT NULL CHECK (rating BETWEEN 0 AND 5), description text NOT NULL DEFAULT '', is_active boolean NOT NULL DEFAULT true);
CREATE TABLE IF NOT EXISTS hotel_features (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), name text NOT NULL UNIQUE);
CREATE TABLE IF NOT EXISTS hotel_feature_links (hotel_id uuid NOT NULL REFERENCES hotels(id) ON DELETE CASCADE, feature_id uuid NOT NULL REFERENCES hotel_features(id) ON DELETE CASCADE, PRIMARY KEY (hotel_id, feature_id));
CREATE TABLE IF NOT EXISTS hotel_rooms (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), hotel_id uuid NOT NULL REFERENCES hotels(id) ON DELETE CASCADE, name text NOT NULL, capacity smallint NOT NULL CHECK (capacity BETWEEN 1 AND 20), features text[] NOT NULL DEFAULT '{}', is_active boolean NOT NULL DEFAULT true);
CREATE UNIQUE INDEX IF NOT EXISTS ux_hotel_rooms_hotel_name ON hotel_rooms(hotel_id, name);
CREATE TABLE IF NOT EXISTS room_daily_rates (room_id uuid NOT NULL REFERENCES hotel_rooms(id) ON DELETE CASCADE, stay_date date NOT NULL, nightly_price numeric(10,2) NOT NULL CHECK (nightly_price > 0), rooms_available integer NOT NULL CHECK (rooms_available >= 0), PRIMARY KEY (room_id, stay_date));
CREATE TABLE IF NOT EXISTS flights (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), flight_number text NOT NULL UNIQUE, airline_id uuid NOT NULL REFERENCES airlines(id), status text NOT NULL DEFAULT 'scheduled' CHECK (status IN ('scheduled','cancelled')), UNIQUE(flight_number));
CREATE TABLE IF NOT EXISTS flight_legs (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), flight_id uuid NOT NULL REFERENCES flights(id) ON DELETE CASCADE, leg_order smallint NOT NULL CHECK (leg_order > 0), departure_airport_id uuid NOT NULL REFERENCES airports(id), arrival_airport_id uuid NOT NULL REFERENCES airports(id), departure_at timestamptz NOT NULL, arrival_at timestamptz NOT NULL, CHECK (departure_airport_id <> arrival_airport_id), CHECK (arrival_at > departure_at), UNIQUE(flight_id, leg_order));
CREATE TABLE IF NOT EXISTS flight_fares (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), flight_id uuid NOT NULL REFERENCES flights(id) ON DELETE CASCADE, name text NOT NULL, price numeric(10,2) NOT NULL CHECK (price > 0), currency char(3) NOT NULL DEFAULT 'TRY', baggage text NOT NULL, change_policy text NOT NULL, seats_available integer NOT NULL CHECK (seats_available >= 0), is_active boolean NOT NULL DEFAULT true, UNIQUE(flight_id, name));

INSERT INTO travel_countries (name, code) VALUES ('Türkiye', 'TR') ON CONFLICT DO NOTHING;
INSERT INTO travel_cities (country_id, name) SELECT id, 'İstanbul' FROM travel_countries WHERE code = 'TR' ON CONFLICT DO NOTHING;
INSERT INTO travel_cities (country_id, name) SELECT id, 'Antalya' FROM travel_countries WHERE code = 'TR' ON CONFLICT DO NOTHING;
INSERT INTO travel_cities (country_id, name) SELECT id, 'Ankara' FROM travel_countries WHERE code = 'TR' ON CONFLICT DO NOTHING;
INSERT INTO travel_cities (country_id, name) SELECT id, 'İzmir' FROM travel_countries WHERE code = 'TR' ON CONFLICT DO NOTHING;
INSERT INTO travel_cities (country_id, name) SELECT id, 'Trabzon' FROM travel_countries WHERE code = 'TR' ON CONFLICT DO NOTHING;
INSERT INTO airports (city_id, name, iata_code) SELECT id, 'İstanbul Havalimanı', 'IST' FROM travel_cities WHERE name = 'İstanbul' ON CONFLICT DO NOTHING;
INSERT INTO airports (city_id, name, iata_code) SELECT id, 'Sabiha Gökçen Havalimanı', 'SAW' FROM travel_cities WHERE name = 'İstanbul' ON CONFLICT DO NOTHING;
INSERT INTO airports (city_id, name, iata_code) SELECT id, 'Antalya Havalimanı', 'AYT' FROM travel_cities WHERE name = 'Antalya' ON CONFLICT DO NOTHING;
INSERT INTO airports (city_id, name, iata_code) SELECT id, 'Esenboğa Havalimanı', 'ESB' FROM travel_cities WHERE name = 'Ankara' ON CONFLICT DO NOTHING;
INSERT INTO airports (city_id, name, iata_code) SELECT id, 'Adnan Menderes Havalimanı', 'ADB' FROM travel_cities WHERE name = 'İzmir' ON CONFLICT DO NOTHING;
INSERT INTO airports (city_id, name, iata_code) SELECT id, 'Trabzon Havalimanı', 'TZX' FROM travel_cities WHERE name = 'Trabzon' ON CONFLICT DO NOTHING;
INSERT INTO airlines (name, iata_code) VALUES ('Türk Hava Yolları', 'TK'), ('Pegasus', 'PC') ON CONFLICT DO NOTHING;
INSERT INTO airlines (name, iata_code) VALUES ('SunExpress', 'XQ'), ('AJet', 'VF') ON CONFLICT DO NOTHING;
INSERT INTO hotel_features (name) VALUES ('Wi-Fi'), ('Kahvaltı'), ('Havuz'), ('Otopark') ON CONFLICT DO NOTHING;
INSERT INTO hotels (name, city_id, district, stars, rating, description) SELECT 'Galata Meydan Otel', id, 'Beyoğlu', 4, 4.6, 'Şehir merkezinde örnek konaklama.' FROM travel_cities WHERE name = 'İstanbul' ON CONFLICT DO NOTHING;
INSERT INTO hotels (name, city_id, district, stars, rating, description) SELECT 'Kalepark Konaklama', id, 'Konyaaltı', 5, 4.8, 'Sahil bölgesinde örnek konaklama.' FROM travel_cities WHERE name = 'Antalya' ON CONFLICT DO NOTHING;
INSERT INTO hotels (name, city_id, district, stars, rating, description) SELECT 'Kordon Butik Otel', id, 'Alsancak', 3, 4.1, 'İzmir merkezinde ekonomik seçenek.' FROM travel_cities WHERE name = 'İzmir' ON CONFLICT DO NOTHING;
INSERT INTO hotels (name, city_id, district, stars, rating, description) SELECT 'Uzungöl Dağ Evi', id, 'Çaykara', 4, 4.4, 'Doğa manzaralı örnek konaklama.' FROM travel_cities WHERE name = 'Trabzon' ON CONFLICT DO NOTHING;
INSERT INTO hotel_rooms (hotel_id, name, capacity, features) SELECT id, 'Standart Oda', 2, ARRAY['Wi-Fi', 'Kahvaltı'] FROM hotels WHERE name = 'Galata Meydan Otel' ON CONFLICT DO NOTHING;
INSERT INTO hotel_rooms (hotel_id, name, capacity, features) SELECT id, 'Aile Odası', 4, ARRAY['Wi-Fi', 'Kahvaltı', 'Otopark'] FROM hotels WHERE name = 'Galata Meydan Otel' ON CONFLICT DO NOTHING;
INSERT INTO hotel_rooms (hotel_id, name, capacity, features) SELECT id, 'Deniz Manzaralı Süit', 3, ARRAY['Wi-Fi', 'Havuz'] FROM hotels WHERE name = 'Kalepark Konaklama' ON CONFLICT DO NOTHING;
INSERT INTO hotel_rooms (hotel_id, name, capacity, features) SELECT id, 'Ekonomik Oda', 2, ARRAY['Wi-Fi'] FROM hotels WHERE name = 'Kordon Butik Otel' ON CONFLICT DO NOTHING;
INSERT INTO hotel_rooms (hotel_id, name, capacity, features) SELECT id, 'Manzara Odası', 2, ARRAY['Wi-Fi', 'Kahvaltı'] FROM hotels WHERE name = 'Uzungöl Dağ Evi' ON CONFLICT DO NOTHING;
INSERT INTO room_daily_rates (room_id, stay_date, nightly_price, rooms_available) SELECT r.id, d::date, CASE WHEN EXTRACT(ISODOW FROM d) IN (6,7) THEN 4200 ELSE 3500 END, 3 FROM hotel_rooms r CROSS JOIN generate_series(current_date, current_date + 180, interval '1 day') d ON CONFLICT DO NOTHING;
UPDATE room_daily_rates SET nightly_price = CASE WHEN EXTRACT(ISODOW FROM stay_date) IN (6,7) THEN 5200 ELSE 3900 END WHERE room_id IN (SELECT id FROM hotel_rooms WHERE name = 'Deniz Manzaralı Süit');
UPDATE room_daily_rates SET rooms_available = 1 WHERE room_id IN (SELECT id FROM hotel_rooms WHERE name = 'Ekonomik Oda') AND stay_date BETWEEN current_date + 3 AND current_date + 7;
UPDATE room_daily_rates SET rooms_available = 0 WHERE room_id IN (SELECT id FROM hotel_rooms WHERE name = 'Manzara Odası') AND stay_date = current_date + 10;
INSERT INTO flights (flight_number, airline_id) SELECT 'TK2001', id FROM airlines WHERE iata_code = 'TK' ON CONFLICT DO NOTHING;
INSERT INTO flights (flight_number, airline_id) SELECT 'PC3102', id FROM airlines WHERE iata_code = 'PC' ON CONFLICT DO NOTHING;
INSERT INTO flights (flight_number, airline_id) SELECT 'XQ901', id FROM airlines WHERE iata_code = 'XQ' ON CONFLICT DO NOTHING;
INSERT INTO flight_legs (flight_id, leg_order, departure_airport_id, arrival_airport_id, departure_at, arrival_at) SELECT f.id, 1, a1.id, a2.id, now() + interval '1 day' + interval '9 hours', now() + interval '1 day' + interval '10 hours 15 minutes' FROM flights f JOIN airports a1 ON a1.iata_code='IST' JOIN airports a2 ON a2.iata_code='AYT' WHERE f.flight_number='TK2001' ON CONFLICT DO NOTHING;
INSERT INTO flight_legs (flight_id, leg_order, departure_airport_id, arrival_airport_id, departure_at, arrival_at) SELECT f.id, 1, a1.id, a2.id, now() + interval '2 days' + interval '14 hours', now() + interval '2 days' + interval '15 hours 10 minutes' FROM flights f JOIN airports a1 ON a1.iata_code='SAW' JOIN airports a2 ON a2.iata_code='ESB' WHERE f.flight_number='PC3102' ON CONFLICT DO NOTHING;
INSERT INTO flight_legs (flight_id, leg_order, departure_airport_id, arrival_airport_id, departure_at, arrival_at) SELECT f.id, 1, a1.id, a2.id, now() + interval '3 days' + interval '7 hours', now() + interval '3 days' + interval '8 hours 20 minutes' FROM flights f JOIN airports a1 ON a1.iata_code='ADB' JOIN airports a2 ON a2.iata_code='TZX' WHERE f.flight_number='XQ901' ON CONFLICT DO NOTHING;
INSERT INTO flight_fares (flight_id, name, price, baggage, change_policy, seats_available) SELECT id, 'Ekonomi Basic', 1800, 'El bagajı 8 kg', 'Değişiklik ücretli', 8 FROM flights WHERE flight_number='TK2001' ON CONFLICT DO NOTHING;
INSERT INTO flight_fares (flight_id, name, price, baggage, change_policy, seats_available) SELECT id, 'Ekonomi Flex', 2600, 'Kabin + 20 kg', 'Bir kez ücretsiz değişiklik', 4 FROM flights WHERE flight_number='TK2001' ON CONFLICT DO NOTHING;
INSERT INTO flight_fares (flight_id, name, price, baggage, change_policy, seats_available) SELECT id, 'Standart', 1200, 'El bagajı 8 kg', 'İade ve değişiklik yok', 12 FROM flights WHERE flight_number='PC3102' ON CONFLICT DO NOTHING;
INSERT INTO flight_fares (flight_id, name, price, baggage, change_policy, seats_available) SELECT id, 'Eco', 1450, 'El bagajı 8 kg', 'Değişiklik ücretli', 1 FROM flights WHERE flight_number='XQ901' ON CONFLICT DO NOTHING;
INSERT INTO flight_fares (flight_id, name, price, baggage, change_policy, seats_available) SELECT id, 'Flex', 2200, 'Kabin + 15 kg', 'Ücretsiz değişiklik', 0 FROM flights WHERE flight_number='XQ901' ON CONFLICT DO NOTHING;

CREATE TABLE IF NOT EXISTS app_metadata (
    key text PRIMARY KEY,
    value text NOT NULL,
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS app_users (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    name text NOT NULL,
    email text NOT NULL UNIQUE,
    password_hash text NOT NULL,
    role text NOT NULL DEFAULT 'user' CHECK (role IN ('user', 'admin')),
    phone text,
    currency char(3) NOT NULL DEFAULT 'TRY',
    created_at timestamptz NOT NULL DEFAULT now()
);

ALTER TABLE app_users ADD COLUMN IF NOT EXISTS phone text;
ALTER TABLE app_users ADD COLUMN IF NOT EXISTS currency char(3) NOT NULL DEFAULT 'TRY';

CREATE TABLE IF NOT EXISTS app_bookings (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL REFERENCES app_users(id) ON DELETE CASCADE,
    kind text NOT NULL CHECK (kind IN ('hotel', 'flight')),
    title text NOT NULL,
    status text NOT NULL DEFAULT 'simulated',
    created_at timestamptz NOT NULL DEFAULT now()
);

INSERT INTO app_metadata (key, value)
VALUES ('schema_version', '1')
ON CONFLICT (key) DO UPDATE
SET value = EXCLUDED.value,
    updated_at = now();
