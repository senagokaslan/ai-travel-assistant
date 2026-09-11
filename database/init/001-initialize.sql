CREATE EXTENSION IF NOT EXISTS pgcrypto;

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
