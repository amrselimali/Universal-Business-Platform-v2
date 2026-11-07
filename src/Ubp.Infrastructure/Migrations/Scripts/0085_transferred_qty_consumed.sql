-- 0085 — إظهار أثر تحويل الخدمات في عمود «مستهلك» بشاشة الخدمات المحجوزة
-- ---------------------------------------------------------------------------
-- عند تحويل كمية خدمة من عميل إلى آخر بنفس الصنف وسعر الوحدة:
--   - للمحوَّل منه: transferred_qty += الكمية (موجب) => «مستهلك» يزيد والمتبقي يقل.
--   - للمحوَّل إليه: transferred_qty -= الكمية (سالب) => «مستهلك» يصبح سالبًا والمتبقي يزيد.
-- ويُعدَّل رصيد المتبقي (reserved_qty) فعليًا في الترحيل نفسه.

ALTER TABLE booking_lines ADD COLUMN IF NOT EXISTS transferred_qty numeric(18,4)
    NOT NULL DEFAULT 0;
COMMENT ON COLUMN booking_lines.transferred_qty IS
    'صافي الكمية المحوَّلة لهذا السطر: موجب (محوَّل منه) / سالب (محوَّل إليه) — 0085.';

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
       -- «المستهلك» = المنفَّذ/المفوتر + صافي التحويل (موجب محوَّل منه / سالب محوَّل إليه).
       (GREATEST(l.delivered_qty, l.invoiced_qty) + l.transferred_qty) AS consumed_qty,
       l.reserved_qty::numeric AS remaining_qty,
       l.unit_price            AS unit_price,
       (l.reserved_qty * l.unit_price)::numeric AS remaining_value
FROM bookings b
JOIN booking_lines l ON l.booking_id = b.id
JOIN customers c ON c.id = b.customer_id
JOIN items i ON i.id = l.item_id
JOIN warehouses w ON w.id = l.warehouse_id
WHERE b.status NOT IN ('cancelled', 'reversed');

COMMENT ON VIEW v_customer_reservations IS
    'كارتة كميات العميل: المحجوز/المستهلك (شامل التحويل)/المتبقي لكل عميل وصنف — 0085.';
