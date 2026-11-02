-- 0064 — تاريخ واحد للحجز: إلغاء تاريخ نهاية الحجز نهائياً
-- تاريخ الحجز booking_date (مع starts_at للجلسة) هو المصدر الوحيد للتقارير والربط بالتنفيذ.

DROP VIEW IF EXISTS v_customer_reservations;
CREATE VIEW v_customer_reservations AS
SELECT b.id                    AS booking_id,
       b.branch_id             AS branch_id,
       b.customer_id           AS customer_id,
       c.name_ar               AS customer_name,
       b.status                AS booking_status,
       b.doc_no                AS booking_no,
       b.deposit_amount        AS deposit_amount,
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

DROP INDEX IF EXISTS ix_bookings_expiry;
ALTER TABLE bookings DROP COLUMN IF EXISTS expires_at;

DELETE FROM reservation_adjustments WHERE kind = 'EXTEND';