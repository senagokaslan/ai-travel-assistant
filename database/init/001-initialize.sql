CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS travel_countries (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), name text NOT NULL UNIQUE, code char(2) NOT NULL UNIQUE);
CREATE TABLE IF NOT EXISTS travel_cities (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), country_id uuid NOT NULL REFERENCES travel_countries(id), name text NOT NULL, UNIQUE(country_id, name));
CREATE TABLE IF NOT EXISTS airports (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), city_id uuid NOT NULL REFERENCES travel_cities(id), name text NOT NULL, iata_code char(3) NOT NULL UNIQUE, is_active boolean NOT NULL DEFAULT true);
CREATE TABLE IF NOT EXISTS airlines (id uuid PRIMARY KEY DEFAULT gen_random_uuid(), name text NOT NULL UNIQUE, iata_code char(2) NOT NULL UNIQUE, is_active boolean NOT NULL DEFAULT true);

INSERT INTO travel_countries (name, code) VALUES ('Türkiye', 'TR') ON CONFLICT DO NOTHING;
INSERT INTO travel_cities (country_id, name) SELECT id, 'İstanbul' FROM travel_countries WHERE code = 'TR' ON CONFLICT DO NOTHING;
INSERT INTO travel_cities (country_id, name) SELECT id, 'Antalya' FROM travel_countries WHERE code = 'TR' ON CONFLICT DO NOTHING;
INSERT INTO travel_cities (country_id, name) SELECT id, 'Ankara' FROM travel_countries WHERE code = 'TR' ON CONFLICT DO NOTHING;
INSERT INTO airports (city_id, name, iata_code) SELECT id, 'İstanbul Havalimanı', 'IST' FROM travel_cities WHERE name = 'İstanbul' ON CONFLICT DO NOTHING;
INSERT INTO airports (city_id, name, iata_code) SELECT id, 'Sabiha Gökçen Havalimanı', 'SAW' FROM travel_cities WHERE name = 'İstanbul' ON CONFLICT DO NOTHING;
INSERT INTO airports (city_id, name, iata_code) SELECT id, 'Antalya Havalimanı', 'AYT' FROM travel_cities WHERE name = 'Antalya' ON CONFLICT DO NOTHING;
INSERT INTO airports (city_id, name, iata_code) SELECT id, 'Esenboğa Havalimanı', 'ESB' FROM travel_cities WHERE name = 'Ankara' ON CONFLICT DO NOTHING;
INSERT INTO airlines (name, iata_code) VALUES ('Türk Hava Yolları', 'TK'), ('Pegasus', 'PC') ON CONFLICT DO NOTHING;

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
