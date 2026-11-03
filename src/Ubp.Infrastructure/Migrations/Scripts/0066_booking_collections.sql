-- 0066 — تحصيل قيمة الخدمات المحجوزة من نقطة البيع
-- مصدر واحد لسداد الحجوزات: كل سند قبض يُخصَّص على الحجز عبر booking_payments،
-- والرصيد المستحق يُقرأ من العرض v_customer_booking_balances.

CREATE TABLE IF NOT EXISTS booking_payments (
    id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    booking_id  uuid NOT NULL REFERENCES bookings(id) ON DELETE RESTRICT,
    voucher_id  uuid NOT NULL REFERENCES vouchers(id) ON DELETE RESTRICT,
    amount      numeric(18,2) NOT NULL CHECK (amount > 0),
    paid_at     timestamptz NOT NULL DEFAULT now(),
    created_by  uuid REFERENCES users(id) ON DELETE SET NULL,
    notes       varchar(500),
    CONSTRAINT ux_booking_payments_voucher_booking UNIQUE (voucher_id, booking_id)
);

CREATE INDEX IF NOT EXISTS ix_booking_payments_booking
    ON booking_payments (booking_id, paid_at DESC);

COMMENT ON TABLE booking_payments IS
    'سداد قيمة حجز/جلسة: يربط سند القبض المرحّل بالحجز والمبلغ المسدد، وأساس حساب «المسدد سابقًا» والمستحق.';

CREATE OR REPLACE VIEW v_customer_booking_balances AS
SELECT b.id                     AS booking_id,
       b.branch_id              AS branch_id,
       b.customer_id            AS customer_id,
       b.doc_no                 AS booking_no,
       b.status                 AS status,
       b.booking_date           AS booking_date,
       b.source                 AS source,
       b.total_amount           AS total_amount,
       (b.deposit_amount + COALESCE(p.paid_amount, 0)) AS paid_amount,
       GREATEST(b.total_amount - b.deposit_amount - COALESCE(p.paid_amount, 0), 0)
                                AS remaining_amount,
       (SELECT string_agg(DISTINCT i.name_ar, ' • ')
        FROM booking_lines l JOIN items i ON i.id = l.item_id
        WHERE l.booking_id = b.id) AS items,
       (SELECT string_agg(DISTINCT i.name_ar, ' • ')
        FROM booking_lines l JOIN items i ON i.id = l.item_id
        WHERE l.booking_id = b.id AND i.item_type = 'SERVICE') AS services
FROM bookings b
LEFT JOIN (
    SELECT booking_id, SUM(amount) AS paid_amount
    FROM booking_payments GROUP BY booking_id
) p ON p.booking_id = b.id
WHERE b.status NOT IN ('cancelled', 'reversed');
