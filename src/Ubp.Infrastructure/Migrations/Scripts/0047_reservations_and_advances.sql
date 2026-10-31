-- 0047 — الحجوزات كدفعات مقدمة + الرصيد البيعي + سجل التحويلات
-- لا يُعدَّل أي ترحيل سابق (G1).

CREATE TABLE reservation_adjustments (
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    booking_id      uuid NOT NULL REFERENCES bookings(id) ON DELETE CASCADE,
    booking_line_id uuid REFERENCES booking_lines(id) ON DELETE CASCADE,
    kind            varchar(20) NOT NULL
                    CHECK (kind IN ('TO_ADVANCE', 'RELEASE', 'EXTEND')),
    quantity        numeric(18,4) NOT NULL DEFAULT 0,
    amount          numeric(18,4) NOT NULL DEFAULT 0,
    voucher_id      uuid REFERENCES vouchers(id) ON DELETE SET NULL,
    journal_entry_id uuid REFERENCES journal_entries(id) ON DELETE SET NULL,
    note            varchar(500),
    created_by      uuid REFERENCES users(id) ON DELETE SET NULL,
    created_at      timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_reservation_adjustments_amount CHECK (amount >= 0),
    CONSTRAINT ck_reservation_adjustments_quantity CHECK (quantity >= 0)
);

-- تحويل واحد فقط لكل سطر حجز إلى دفعة مقدمة (منع الازدواج)
CREATE UNIQUE INDEX ux_reservation_adjustments_line_kind
    ON reservation_adjustments (booking_line_id, kind)
    WHERE booking_line_id IS NOT NULL AND kind = 'TO_ADVANCE';

CREATE INDEX ix_reservation_adjustments_booking ON reservation_adjustments (booking_id);

COMMENT ON TABLE reservation_adjustments IS
    'سجل تحويل/تحرير/تمديد الحجوزات: التحويل إلى دفعة مقدمة يُنشئ سند قبض ويُربط هنا لمنع التكرار.';

-- الكميات المحجوزة/المستهلكة/المتبقية لكل عميل وصنف وفرع
CREATE OR REPLACE VIEW v_customer_reservations AS
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
       l.reserved_qty          AS reserved_qty,
       (l.delivered_qty + l.invoiced_qty) AS consumed_qty,
       GREATEST(l.reserved_qty - (l.delivered_qty + l.invoiced_qty), 0) AS remaining_qty,
       l.unit_price            AS unit_price,
       GREATEST(l.reserved_qty - (l.delivered_qty + l.invoiced_qty), 0) * l.unit_price
                               AS remaining_value
FROM bookings b
JOIN booking_lines l ON l.booking_id = b.id
JOIN customers c ON c.id = b.customer_id
JOIN items i ON i.id = l.item_id
JOIN warehouses w ON w.id = l.warehouse_id
WHERE b.status NOT IN ('cancelled', 'reversed');

COMMENT ON VIEW v_customer_reservations IS
    'ما حُجز للعميل وما استُهلك وما بقِي غير مستخدم (أساس شاشة الحجوزات والتحويل إلى دفعة مقدمة).';

-- الرصيد القابل للبيع = المتاح ناقص المحجوز
CREATE OR REPLACE VIEW v_item_sellable AS
SELECT b.item_id,
       b.warehouse_id,
       b.on_hand,
       COALESCE(r.reserved, 0)                    AS reserved_qty,
       b.on_hand - COALESCE(r.reserved, 0)        AS sellable_qty
FROM stock_balances b
LEFT JOIN (
    SELECT item_id, warehouse_id, SUM(reserved_qty) AS reserved
    FROM booking_lines l
    JOIN bookings g ON g.id = l.booking_id
    WHERE g.status IN ('confirmed', 'draft')
    GROUP BY item_id, warehouse_id
) r ON r.item_id = b.item_id AND r.warehouse_id = b.warehouse_id;

COMMENT ON VIEW v_item_sellable IS
    'الرصيد البيعي المتاح بعد خصم الكميات المحجوزة للعملاء.';
