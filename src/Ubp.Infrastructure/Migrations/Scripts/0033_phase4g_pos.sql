-- 0033 — المرحلة 4G: نقطة البيع (لا جداول جديدة)
-- -----------------------------------------------------------------------------

CREATE INDEX IF NOT EXISTS ix_bookings_source ON bookings (source, booking_date DESC);
CREATE INDEX IF NOT EXISTS ix_vouchers_pos ON vouchers (branch_id, doc_date DESC)
    WHERE voucher_type = 'receipt';
