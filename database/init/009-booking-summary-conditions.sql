ALTER TABLE hotels
ADD COLUMN IF NOT EXISTS cancellation_policy text NOT NULL
DEFAULT 'Girişten 48 saat öncesine kadar ücretsiz iptal; sonrasında ilk gece bedeli uygulanır.';

UPDATE hotels
SET cancellation_policy = CASE
    WHEN stars >= 5 THEN 'Girişten 72 saat öncesine kadar ücretsiz iptal; sonrasında ilk gece bedeli uygulanır.'
    WHEN name = 'Kapadokya Taş Konak' THEN 'Girişten 7 gün öncesine kadar ücretsiz iptal; sonrasında toplam tutarın %50''si uygulanır.'
    ELSE 'Girişten 48 saat öncesine kadar ücretsiz iptal; sonrasında ilk gece bedeli uygulanır.'
END;
