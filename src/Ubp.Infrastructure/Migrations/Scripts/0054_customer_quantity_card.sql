-- 0054 — كارتة كميات العميل مستقلة عن حجز المخزون
-- كمية الحجز للعميل تُسجَّل دائمًا في booking_lines.reserved_qty، بينما
-- stock_balances.reserved_qty يظل محصورًا في إعداد reserve_stock_on_booking.

-- Backfill: الحجوزات النشطة القديمة التي لم تُسجّل كمية العميل (setting off سابقاً).
UPDATE booking_lines l
   SET reserved_qty = GREATEST(l.quantity - l.delivered_qty - l.cancelled_qty, 0)
  FROM bookings b
 WHERE b.id = l.booking_id
   AND b.status IN ('confirmed', 'partially_delivered')
   AND l.reserved_qty = 0
   AND l.quantity > 0;

-- المحجوز للعميل = كمية الحجز الأصلية؛ المتبقي = reserved_qty الحالي (يُخفَّض عند التنفيذ)؛
-- والمستهلك = أكبر من delivered و invoiced (المساران لا يُجمعان لنفس الكمية).
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

COMMENT ON VIEW v_customer_reservations IS
    'كارتة كميات العميل: المحجوز الحالي/المستهلك/المتبقي لكل عميل وصنف؛ مستقلة عن حجز المخزون.';

-- الرصيد القابل للبيع يعتمد على حجز المخزون الفعلي في stock_balances فقط.
DROP VIEW IF EXISTS v_item_sellable;
CREATE VIEW v_item_sellable AS
SELECT b.item_id,
       b.warehouse_id,
       b.on_hand,
       b.reserved_qty,
       GREATEST(b.on_hand - b.reserved_qty, 0) AS sellable_qty
FROM stock_balances b;

COMMENT ON VIEW v_item_sellable IS
    'الرصيد البيعي المتاح بعد خصم حجز المخزون الفعلي (وليس كمية العميل).';
