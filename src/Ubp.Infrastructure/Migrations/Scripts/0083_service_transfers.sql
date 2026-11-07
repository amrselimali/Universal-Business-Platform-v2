-- 0083 — تحويل خدمات/جلسات بين العملاء من نقطة البيع مع ترحيل مؤجَّل للمراجعة
-- ---------------------------------------------------------------------------
-- 1) مصدر حجز جديد «transfer» للحجز الذي يُنشأ للعميل المحوَّل إليه.
-- 2) نوع مصدر قيد جديد «service_transfer».
-- 3) service_transfers / service_transfer_lines: مستند تحويل يبقى مسودةً بلا أي أثر
--    على الحسابات أو أرصدة العميل حتى ترحيله/اعتماده من المراجعة، وعندها:
--    - تُخصم الكمية من رصيد العميل المحوَّل منه وتُضاف لرصيد العميل المحوَّل إليه.
--    - يُنشأ قيد من حساب العميل المحوَّل إليه (مدين) إلى حساب العميل المحوَّل منه (دائن).

ALTER TABLE bookings DROP CONSTRAINT IF EXISTS bookings_source_check;
ALTER TABLE bookings ADD CONSTRAINT bookings_source_check
    CHECK (source IN ('manual', 'pos', 'transfer'));

ALTER TABLE journal_entries DROP CONSTRAINT IF EXISTS ck_journal_entries_source_type;
ALTER TABLE journal_entries ADD CONSTRAINT ck_journal_entries_source_type
    CHECK (source_type IN ('manual', 'stock_document', 'stocktake', 'opening', 'invoice',
                           'voucher', 'cash_shift', 'delivery', 'execution',
                           'operating_expense', 'cash_handover', 'service_transfer'));

CREATE TABLE IF NOT EXISTS service_transfers (
    id                uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    branch_id         uuid NOT NULL REFERENCES branches(id) ON DELETE RESTRICT,
    doc_no            varchar(30),
    doc_date          date NOT NULL,
    from_customer_id  uuid NOT NULL REFERENCES customers(id) ON DELETE RESTRICT,
    to_customer_id    uuid NOT NULL REFERENCES customers(id) ON DELETE RESTRICT,
    status            varchar(10) NOT NULL DEFAULT 'draft' CHECK (status IN ('draft', 'posted')),
    total_quantity    numeric(18,4) NOT NULL DEFAULT 0 CHECK (total_quantity >= 0),
    total_amount      numeric(18,2) NOT NULL DEFAULT 0 CHECK (total_amount >= 0),
    journal_entry_id  uuid REFERENCES journal_entries(id) ON DELETE SET NULL,
    target_booking_id uuid REFERENCES bookings(id) ON DELETE SET NULL,
    notes             varchar(500),
    created_at        timestamptz NOT NULL DEFAULT now(),
    created_by        uuid REFERENCES users(id) ON DELETE SET NULL,
    posted_at         timestamptz,
    posted_by         uuid REFERENCES users(id) ON DELETE SET NULL,
    CONSTRAINT ck_service_transfers_customers CHECK (from_customer_id <> to_customer_id)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_service_transfer_no
    ON service_transfers (branch_id, doc_no) WHERE doc_no IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_service_transfers_branch_date
    ON service_transfers (branch_id, created_at DESC);

COMMENT ON TABLE service_transfers IS
    'تحويل خدمات/جلسات من عميل إلى آخر: مسودة بلا أثر حتى الترحيل، وعند الترحيل يُخصم من رصيد '
    'المحوَّل منه ويُضاف للمحوَّل إليه مع قيد من حساب العميل المحوَّل إليه إلى حساب المحوَّل منه.';

CREATE TABLE IF NOT EXISTS service_transfer_lines (
    id                   uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    transfer_id          uuid NOT NULL REFERENCES service_transfers(id) ON DELETE CASCADE,
    line_no              integer NOT NULL CHECK (line_no > 0),
    from_booking_line_id uuid NOT NULL REFERENCES booking_lines(id) ON DELETE RESTRICT,
    item_id              uuid NOT NULL REFERENCES items(id) ON DELETE RESTRICT,
    unit_id              uuid NOT NULL REFERENCES units(id) ON DELETE RESTRICT,
    warehouse_id         uuid NOT NULL REFERENCES warehouses(id) ON DELETE RESTRICT,
    quantity             numeric(18,4) NOT NULL CHECK (quantity > 0),
    unit_price           numeric(18,4) NOT NULL CHECK (unit_price >= 0),
    amount               numeric(18,2) NOT NULL DEFAULT 0 CHECK (amount >= 0),
    CONSTRAINT ux_service_transfer_lines UNIQUE (transfer_id, line_no)
);

CREATE INDEX IF NOT EXISTS ix_service_transfer_lines_transfer
    ON service_transfer_lines (transfer_id);
CREATE INDEX IF NOT EXISTS ix_service_transfer_lines_src
    ON service_transfer_lines (from_booking_line_id);

COMMENT ON TABLE service_transfer_lines IS
    'سطور تحويل الخدمات: سطر المصدر في حجز العميل المحوَّل منه والكمية المحوَّلة وسعر الوحدة.';
