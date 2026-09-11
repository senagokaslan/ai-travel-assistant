CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS travel_countries (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), name text NOT NULL UNIQUE, code char(2) NOT NULL UNIQUE);
CREATE TABLE IF NOT EXISTS travel_cities (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), country_id uuid NOT NULL REFERENCES travel_countries(id), name text NOT NULL, UNIQUE(country_id, name));
CREATE TABLE IF NOT EXISTS airports (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), city_id uuid NOT NULL REFERENCES travel_cities(id), name text NOT NULL, iata_code char(3) NOT NULL UNIQUE, is_active boolean NOT NULL DEFAULT true);
CREATE TABLE IF NOT EXISTS airlines (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), name text NOT NULL UNIQUE, iata_code char(2) NOT NULL UNIQUE, is_active boolean NOT NULL DEFAULT true);
CREATE TABLE IF NOT EXISTS hotels (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), name text NOT NULL, city_id uuid NOT NULL REFERENCES travel_cities(id), district text NOT NULL, stars smallint NOT NULL CHECK (stars BETWEEN 1 AND 5), rating numeric(2,1) NOT NULL CHECK (rating BETWEEN 0 AND 5), description text NOT NULL DEFAULT '', is_active boolean NOT NULL DEFAULT true);
CREATE TABLE IF NOT EXISTS hotel_features (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), name text NOT NULL UNIQUE);
CREATE TABLE IF NOT EXISTS hotel_feature_links (hotel_id uuid NOT NULL REFERENCES hotels(id) ON DELETE CASCADE, feature_id uuid NOT NULL REFERENCES hotel_features(id) ON DELETE CASCADE, PRIMARY KEY (hotel_id, feature_id));
CREATE TABLE IF NOT EXISTS hotel_rooms (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), hotel_id uuid NOT NULL REFERENCES hotels(id) ON DELETE CASCADE, name text NOT NULL, capacity smallint NOT NULL CHECK (capacity BETWEEN 1 AND 20), features text[] NOT NULL DEFAULT '{}', is_active boolean NOT NULL DEFAULT true);
CREATE TABLE IF NOT EXISTS room_daily_rates (room_id uuid NOT NULL REFERENCES hotel_rooms(id) ON DELETE CASCADE, stay_date date NOT NULL, nightly_price numeric(10,2) NOT NULL CHECK (nightly_price > 0), rooms_available integer NOT NULL CHECK (rooms_available >= 0), PRIMARY KEY (room_id, stay_date));

INSERT INTO travel_countries (name, code) VALUES ('Türkiye', 'TR') ON CONFLICT DO NOTHING;
INSERT INTO travel_cities (country_id, name) SELECT id, 'İstanbul' FROM travel_countries WHERE code = 'TR' ON CONFLICT DO NOTHING;
INSERT INTO travel_cities (country_id, name) SELECT id, 'Antalya' FROM travel_countries WHERE code = 'TR' ON CONFLICT DO NOTHING;
INSERT INTO travel_cities (country_id, name) SELECT id, 'Ankara' FROM travel_countries WHERE code = 'TR' ON CONFLICT DO NOTHING;
INSERT INTO airports (city_id, name, iata_code) SELECT id, 'İstanbul Havalimanı', 'IST' FROM travel_cities WHERE name = 'İstanbul' ON CONFLICT DO NOTHING;
INSERT INTO airports (city_id, name, iata_code) SELECT id, 'Sabiha Gökçen Havalimanı', 'SAW' FROM travel_cities WHERE name = 'İstanbul' ON CONFLICT DO NOTHING;
INSERT INTO airports (city_id, name, iata_code) SELECT id, 'Antalya Havalimanı', 'AYT' FROM travel_cities WHERE name = 'Antalya' ON CONFLICT DO NOTHING;
INSERT INTO airports (city_id, name, iata_code) SELECT id, 'Esenboğa Havalimanı', 'ESB' FROM travel_cities WHERE name = 'Ankara' ON CONFLICT DO NOTHING;
INSERT INTO airlines (name, iata_code) VALUES ('Türk Hava Yolları', 'TK'), ('Pegasus', 'PC') ON CONFLICT DO NOTHING;
INSERT INTO hotel_features (name) VALUES ('Wi-Fi'), ('Kahvaltı'), ('Havuz'), ('Otopark') ON CONFLICT DO NOTHING;
INSERT INTO hotels (name, city_id, district, stars, rating, description) SELECT 'Galata Meydan Otel', id, 'Beyoğlu', 4, 4.6, 'Şehir merkezinde örnek konaklama.' FROM travel_cities WHERE name = 'İstanbul' ON CONFLICT DO NOTHING;
INSERT INTO hotels (name, city_id, district, stars, rating, description) SELECT 'Kalepark Konaklama', id, 'Konyaaltı', 5, 4.8, 'Sahil bölgesinde örnek konaklama.' FROM travel_cities WHERE name = 'Antalya' ON CONFLICT DO NOTHING;
INSERT INTO hotel_rooms (hotel_id, name, capacity, features) SELECT id, 'Standart Oda', 2, ARRAY['Wi-Fi', 'Kahvaltı'] FROM hotels WHERE name = 'Galata Meydan Otel' ON CONFLICT DO NOTHING;
INSERT INTO hotel_rooms (hotel_id, name, capacity, features) SELECT id, 'Aile Odası', 4, ARRAY['Wi-Fi', 'Kahvaltı', 'Otopark'] FROM hotels WHERE name = 'Galata Meydan Otel' ON CONFLICT DO NOTHING;
INSERT INTO hotel_rooms (hotel_id, name, capacity, features) SELECT id, 'Deniz Manzaralı Süit', 3, ARRAY['Wi-Fi', 'Havuz'] FROM hotels WHERE name = 'Kalepark Konaklama' ON CONFLICT DO NOTHING;
INSERT INTO room_daily_rates (room_id, stay_date, nightly_price, rooms_available) SELECT r.id, d::date, CASE WHEN EXTRACT(ISODOW FROM d) IN (6,7) THEN 4200 ELSE 3500 END, 3 FROM hotel_rooms r CROSS JOIN generate_series(current_date, current_date + 180, interval '1 day') d ON CONFLICT DO NOTHING;

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
