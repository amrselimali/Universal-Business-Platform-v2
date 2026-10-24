-- 0035 — حجز موعد بوقت من/إلى بدون أسعار أو كميات (خدمات)
-- -----------------------------------------------------------------------------

ALTER TABLE bookings ADD COLUMN IF NOT EXISTS starts_at timestamptz;
ALTER TABLE bookings ADD COLUMN IF NOT EXISTS ends_at timestamptz;

ALTER TABLE bookings DROP CONSTRAINT IF EXISTS ck_booking_time;
ALTER TABLE bookings ADD CONSTRAINT ck_booking_time
    CHECK (starts_at IS NULL OR ends_at IS NULL OR ends_at > starts_at);

CREATE INDEX IF NOT EXISTS ix_bookings_starts ON bookings (starts_at)
    WHERE starts_at IS NOT NULL;
