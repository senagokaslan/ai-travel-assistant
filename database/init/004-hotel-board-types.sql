ALTER TABLE hotels
    ADD COLUMN IF NOT EXISTS board_types text[] NOT NULL DEFAULT ARRAY['room']::text[];

UPDATE hotels SET board_types = ARRAY['room', 'breakfast'] WHERE name = 'Galata Meydan Otel';
UPDATE hotels SET board_types = ARRAY['breakfast', 'half', 'all'] WHERE name = 'Kalepark Konaklama';
UPDATE hotels SET board_types = ARRAY['room', 'breakfast'] WHERE name = 'Kordon Butik Otel';
UPDATE hotels SET board_types = ARRAY['room', 'breakfast', 'half'] WHERE name = 'Uzungöl Dağ Evi';
