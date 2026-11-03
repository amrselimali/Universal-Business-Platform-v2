-- 0065 — إعادة وقت انتهاء صلاحية العرض (expires_at) للحجوزات
-- يُسجَّل من نقطة البيع (افتراضي 6 أشهر)، ويُطبع في إيصال الحجز، ويُمدَّد من
-- شاشتي الحجوزات والدفعات المقدمة. القيمة NULL تعني حجزاً بلا تاريخ انتهاء.

ALTER TABLE bookings ADD COLUMN IF NOT EXISTS expires_at timestamptz;

COMMENT ON COLUMN bookings.expires_at IS
    'وقت انتهاء صلاحية العرض/الحجز؛ يُسجَّل من نقطة البيع ويُمدَّد من الحجوزات والدفعات المقدمة.';

CREATE INDEX IF NOT EXISTS ix_bookings_expiry ON bookings (expires_at);

DROP VIEW IF EXISTS v_customer_reservations;
CREATE VIEW v_customer_reservations AS
SELECT b.id                    AS booking_id,
       b.branch_id             AS branch_id,
       b.customer_id           AS customer_id,
       c.name_ar               AS customer_name,
       b.status                AS booking_status,
       b.doc_no                AS booking_no,
       b.deposit_amount        AS deposit_amount,
       b.expires_at            AS expires_at,
       l.id                    AS booking_line_id,
       l.item_id               AS item_id,
       i.code                  AS item_code,
       i.name_ar               AS item_name,
       l.warehouse_id          AS warehouse_id,
       w.name_ar               AS warehouse_name,
       l.quantity              AS booked_qty,
       GREATEST(l.quantity - l.cancelled_qty, 0)::numeric AS reserved_qty,
       GREATEST(l.delivered_qty, l.invoiced_qty) AS consumed_qty,
       l.reserved_qty::numeric AS remaining_qty,
       l.unit_price            AS unit_price,
       (l.reserved_qty * l.unit_price)::numeric AS remaining_value
FROM bookings b
JOIN booking_lines l ON l.booking_id = b.id
JOIN customers c ON c.id = b.customer_id
JOIN items i ON i.id = l.item_id
JOIN warehouses w ON w.id = l.warehouse_id
WHERE b.status NOT IN ('cancelled', 'reversed');
