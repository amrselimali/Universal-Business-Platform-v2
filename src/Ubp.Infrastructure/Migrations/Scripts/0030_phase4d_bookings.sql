-- 0030 — المرحلة 4D: الحجوزات والمواعيد وإذون التنفيذ واستهلاك الخدمات
-- -----------------------------------------------------------------------------

CREATE EXTENSION IF NOT EXISTS btree_gist;   -- منع تداخل مواعيد الموظف (D10)

CREATE TABLE bookings (
    id                    uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    version               integer     NOT NULL DEFAULT 1,
    created_at            timestamptz NOT NULL DEFAULT now(),
    created_by            uuid REFERENCES users(id) ON DELETE SET NULL,
    updated_at            timestamptz,
    updated_by            uuid REFERENCES users(id) ON DELETE SET NULL,
    branch_id             uuid NOT NULL REFERENCES branches(id) ON DELETE RESTRICT,
    doc_no                varchar(30),
    booking_date          date NOT NULL,
    status                varchar(20) NOT NULL DEFAULT 'draft' CHECK (status IN
                          ('draft','confirmed','partially_delivered','completed','cancelled','expired')),
    notes                 varchar(500),
    posted_at             timestamptz,
    posted_by             uuid REFERENCES users(id) ON DELETE SET NULL,
    journal_entry_id      uuid REFERENCES journal_entries(id) ON DELETE RESTRICT,
    reversed_at           timestamptz,
    reversed_by           uuid REFERENCES users(id) ON DELETE SET NULL,
    reverse_reason        varchar(300),
    customer_id           uuid NOT NULL REFERENCES customers(id) ON DELETE RESTRICT,
    source                varchar(10) NOT NULL DEFAULT 'manual' CHECK (source IN ('manual','pos')),
    due_date              date,
    expires_at            timestamptz,
    assigned_employee_id  uuid REFERENCES employees(id) ON DELETE SET NULL,
    related_booking_id    uuid REFERENCES bookings(id) ON DELETE RESTRICT,
    total_amount          numeric(18,2) NOT NULL DEFAULT 0 CHECK (total_amount >= 0),
    deposit_amount        numeric(18,2) NOT NULL DEFAULT 0 CHECK (deposit_amount >= 0)
);

CREATE UNIQUE INDEX ux_booking_no ON bookings (branch_id, doc_no) WHERE doc_no IS NOT NULL;
CREATE INDEX ix_bookings_customer ON bookings (customer_id, status, booking_date DESC);
CREATE INDEX ix_bookings_employee ON bookings (assigned_employee_id, booking_date DESC);

CREATE TRIGGER trg_touch_bookings BEFORE UPDATE ON bookings
FOR EACH ROW EXECUTE FUNCTION fn_touch_version();

CREATE TABLE booking_lines (
    id                uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    booking_id        uuid NOT NULL REFERENCES bookings(id) ON DELETE CASCADE,
    line_no           integer NOT NULL CHECK (line_no > 0),
    item_id           uuid NOT NULL REFERENCES items(id) ON DELETE RESTRICT,
    unit_id           uuid NOT NULL REFERENCES units(id) ON DELETE RESTRICT,
    warehouse_id      uuid NOT NULL REFERENCES warehouses(id) ON DELETE RESTRICT,
    quantity          numeric(18,4) NOT NULL CHECK (quantity > 0),
    unit_price        numeric(18,4) NOT NULL CHECK (unit_price >= 0),
    discount_percent  numeric(5,2) NOT NULL DEFAULT 0 CHECK (discount_percent BETWEEN 0 AND 100),
    tax_id            uuid REFERENCES taxes(id) ON DELETE RESTRICT,
    net_amount        numeric(18,2) NOT NULL DEFAULT 0,
    tax_amount        numeric(18,2) NOT NULL DEFAULT 0,
    total_amount      numeric(18,2) NOT NULL DEFAULT 0,
    delivered_qty     numeric(18,4) NOT NULL DEFAULT 0 CHECK (delivered_qty >= 0),
    invoiced_qty      numeric(18,4) NOT NULL DEFAULT 0 CHECK (invoiced_qty >= 0),
    cancelled_qty     numeric(18,4) NOT NULL DEFAULT 0 CHECK (cancelled_qty >= 0),
    reserved_qty      numeric(18,4) NOT NULL DEFAULT 0 CHECK (reserved_qty >= 0),
    created_at        timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ux_booking_lines UNIQUE (booking_id, line_no)
);

CREATE INDEX ix_booking_lines_item ON booking_lines (item_id, warehouse_id);

CREATE TABLE booking_assignments (
    id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    booking_id    uuid NOT NULL REFERENCES bookings(id) ON DELETE CASCADE,
    employee_id   uuid NOT NULL REFERENCES employees(id) ON DELETE RESTRICT,
    role          varchar(10) NOT NULL CHECK (role IN ('owner','executor')),
    assigned_at   timestamptz NOT NULL DEFAULT now(),
    unassigned_at timestamptz,
    assigned_by   uuid REFERENCES users(id) ON DELETE SET NULL,
    notes         varchar(300)
);


CREATE TABLE appointments (
    id                uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    is_active         boolean NOT NULL DEFAULT true,
    version           integer NOT NULL DEFAULT 1,
    created_at        timestamptz NOT NULL DEFAULT now(),
    created_by        uuid REFERENCES users(id) ON DELETE SET NULL,
    updated_at        timestamptz,
    updated_by        uuid REFERENCES users(id) ON DELETE SET NULL,
    branch_id         uuid NOT NULL REFERENCES branches(id) ON DELETE RESTRICT,
    employee_id       uuid NOT NULL REFERENCES employees(id) ON DELETE RESTRICT,
    booking_id        uuid REFERENCES bookings(id) ON DELETE SET NULL,
    customer_id       uuid REFERENCES customers(id) ON DELETE SET NULL,
    starts_at         timestamptz NOT NULL,
    ends_at           timestamptz NOT NULL,
    appointment_range tstzrange GENERATED ALWAYS AS (tstzrange(starts_at, ends_at, '[)')) STORED,
    status            varchar(15) NOT NULL DEFAULT 'scheduled' CHECK (status IN
                      ('scheduled','confirmed','in_progress','done','cancelled','no_show')),
    notes             varchar(500),
    CONSTRAINT ck_appt_range CHECK (ends_at > starts_at)
);

ALTER TABLE appointments ADD CONSTRAINT ex_appt_no_overlap
    EXCLUDE USING gist (employee_id WITH =, appointment_range WITH &&)
    WHERE (status <> 'cancelled');

CREATE INDEX ix_appt_day ON appointments (branch_id, starts_at);

CREATE TRIGGER trg_touch_appointments BEFORE UPDATE ON appointments
FOR EACH ROW EXECUTE FUNCTION fn_touch_version();

CREATE TABLE delivery_notes (
    id                uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    version           integer NOT NULL DEFAULT 1,
    created_at        timestamptz NOT NULL DEFAULT now(),
    created_by        uuid REFERENCES users(id) ON DELETE SET NULL,
    updated_at        timestamptz,
    updated_by        uuid REFERENCES users(id) ON DELETE SET NULL,
    branch_id         uuid NOT NULL REFERENCES branches(id) ON DELETE RESTRICT,
    doc_no            varchar(30),
    doc_date          date NOT NULL,
    status            varchar(10) NOT NULL DEFAULT 'draft'
                      CHECK (status IN ('draft','posted','cancelled')),
    notes             varchar(500),
    posted_at         timestamptz,
    posted_by         uuid REFERENCES users(id) ON DELETE SET NULL,
    journal_entry_id  uuid REFERENCES journal_entries(id) ON DELETE RESTRICT,
    booking_id        uuid NOT NULL REFERENCES bookings(id) ON DELETE RESTRICT,
    warehouse_id      uuid NOT NULL REFERENCES warehouses(id) ON DELETE RESTRICT,
    stock_document_id uuid REFERENCES stock_documents(id) ON DELETE RESTRICT,
    invoice_id        uuid REFERENCES commercial_documents(id) ON DELETE RESTRICT
);

CREATE UNIQUE INDEX ux_delivery_no ON delivery_notes (branch_id, doc_no) WHERE doc_no IS NOT NULL;
CREATE INDEX ix_delivery_booking ON delivery_notes (booking_id, doc_date DESC);

CREATE TRIGGER trg_touch_delivery_notes BEFORE UPDATE ON delivery_notes
FOR EACH ROW EXECUTE FUNCTION fn_touch_version();

CREATE TABLE delivery_note_lines (
    id                     uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    delivery_note_id       uuid NOT NULL REFERENCES delivery_notes(id) ON DELETE CASCADE,
    line_no                integer NOT NULL CHECK (line_no > 0),
    booking_line_id        uuid NOT NULL REFERENCES booking_lines(id) ON DELETE RESTRICT,
    item_id                uuid NOT NULL REFERENCES items(id) ON DELETE RESTRICT,
    unit_id                uuid NOT NULL REFERENCES units(id) ON DELETE RESTRICT,
    quantity               numeric(18,4) NOT NULL CHECK (quantity > 0),
    unit_cost              numeric(18,6) NOT NULL DEFAULT 0 CHECK (unit_cost >= 0),
    cost_total             numeric(18,2) NOT NULL DEFAULT 0 CHECK (cost_total >= 0),
    stock_document_line_id uuid REFERENCES stock_document_lines(id) ON DELETE SET NULL,
    created_at             timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ux_delivery_lines UNIQUE (delivery_note_id, line_no)
);

CREATE TABLE service_consumptions (
    id                     uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    delivery_note_id       uuid NOT NULL REFERENCES delivery_notes(id) ON DELETE CASCADE,
    booking_line_id        uuid NOT NULL REFERENCES booking_lines(id) ON DELETE RESTRICT,
    component_item_id      uuid NOT NULL REFERENCES items(id) ON DELETE RESTRICT,
    unit_id                uuid NOT NULL REFERENCES units(id) ON DELETE RESTRICT,
    quantity               numeric(18,4) NOT NULL CHECK (quantity > 0),
    unit_cost              numeric(18,6) NOT NULL DEFAULT 0 CHECK (unit_cost >= 0),
    cost_total             numeric(18,2) NOT NULL DEFAULT 0 CHECK (cost_total >= 0),
    stock_document_line_id uuid REFERENCES stock_document_lines(id) ON DELETE SET NULL,
    created_at             timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_booking_assignments ON booking_assignments (booking_id, assigned_at DESC);

-- نوع مصدر جديد لقيود تكلفة التنفيذ (N4)
ALTER TABLE journal_entries DROP CONSTRAINT IF EXISTS ck_journal_entries_source_type;
ALTER TABLE journal_entries
    ADD CONSTRAINT ck_journal_entries_source_type
    CHECK (source_type IN ('manual', 'stock_document', 'stocktake', 'opening', 'invoice',
                           'voucher', 'cash_shift', 'delivery'));
