-- 0055 — فهرس تاريخ نهاية الحجز لشاشة الحجوزات المنتهية
CREATE INDEX IF NOT EXISTS ix_bookings_expiry
    ON bookings (expires_at)
    WHERE expires_at IS NOT NULL;
