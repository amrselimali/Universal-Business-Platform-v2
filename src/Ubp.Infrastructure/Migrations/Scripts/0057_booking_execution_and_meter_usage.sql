-- 0057 — تنفيذ الحجز مباشرة + استهلاك عدّاد الأصول + اسم عمود الوحدات
ALTER TABLE app_settings
    ADD COLUMN IF NOT EXISTS meter_unit_label varchar(100) NOT NULL DEFAULT 'عدد الوحدات';

CREATE TABLE booking_executions (
    id                uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    branch_id         uuid NOT NULL REFERENCES branches(id) ON DELETE RESTRICT,
    doc_no            varchar(30),
    execution_date    date NOT NULL,
    booking_id        uuid NOT NULL REFERENCES bookings(id) ON DELETE RESTRICT,
    status            varchar(10) NOT NULL DEFAULT 'posted'
                      CHECK (status IN ('posted', 'cancelled')),
    stock_document_id uuid REFERENCES stock_documents(id) ON DELETE RESTRICT,
    journal_entry_id  uuid REFERENCES journal_entries(id) ON DELETE RESTRICT,
    notes             varchar(500),
    created_by        uuid REFERENCES users(id) ON DELETE SET NULL,
    created_at        timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX ux_booking_execution_no
    ON booking_executions (branch_id, doc_no) WHERE doc_no IS NOT NULL;
CREATE INDEX ix_booking_executions_booking
    ON booking_executions (booking_id, execution_date DESC);

CREATE TABLE booking_execution_lines (
    id                     uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    execution_id           uuid NOT NULL REFERENCES booking_executions(id) ON DELETE CASCADE,
    booking_line_id        uuid NOT NULL REFERENCES booking_lines(id) ON DELETE RESTRICT,
    item_id                uuid NOT NULL REFERENCES items(id) ON DELETE RESTRICT,
    unit_id                uuid NOT NULL REFERENCES units(id) ON DELETE RESTRICT,
    quantity               numeric(18,4) NOT NULL CHECK (quantity > 0),
    unit_cost              numeric(18,6) NOT NULL DEFAULT 0 CHECK (unit_cost >= 0),
    cost_total             numeric(18,2) NOT NULL DEFAULT 0 CHECK (cost_total >= 0),
    stock_document_line_id uuid REFERENCES stock_document_lines(id) ON DELETE SET NULL,
    metered_asset_id       uuid REFERENCES fixed_assets(id) ON DELETE SET NULL,
    meter_units            numeric(18,2) NOT NULL DEFAULT 0 CHECK (meter_units >= 0),
    created_at             timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_booking_execution_lines_booking_line
    ON booking_execution_lines (booking_line_id);
CREATE INDEX ix_booking_execution_lines_execution
    ON booking_execution_lines (execution_id);

-- قراءة عدّاد ناتجة عن تنفيذ حجز
ALTER TABLE asset_meter_readings DROP CONSTRAINT IF EXISTS asset_meter_readings_source_check;
ALTER TABLE asset_meter_readings ADD CONSTRAINT asset_meter_readings_source_check
    CHECK (source IN ('MANUAL', 'SALES', 'MAINTENANCE_RESET', 'EXECUTION'));

ALTER TABLE journal_entries DROP CONSTRAINT IF EXISTS ck_journal_entries_source_type;
ALTER TABLE journal_entries ADD CONSTRAINT ck_journal_entries_source_type
    CHECK (source_type IN ('manual', 'stock_document', 'stocktake', 'opening', 'invoice',
                           'voucher', 'cash_shift', 'delivery', 'execution'));

COMMENT ON TABLE booking_executions IS
    'تنفيذ حجز من شاشة الحجوزات؛ بديل إذن التنفيذ القديم، ويحمل تكلفة التنفيذ ووحدات العدّاد.';
