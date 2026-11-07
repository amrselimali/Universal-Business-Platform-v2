-- 0084 — جلسات هدية/علاجية + سلف الموظفين + تعديلات المراجعة والقيد العكسي
-- ---------------------------------------------------------------------------

-- (1) الموظف: حساب تحميل قيمة الجلسة العلاجية على الموظف.
ALTER TABLE employees ADD COLUMN IF NOT EXISTS treatment_account_id uuid
    REFERENCES accounts(id) ON DELETE SET NULL;
COMMENT ON COLUMN employees.treatment_account_id IS
    'حساب تحميل قيمة الجلسة العلاجية على الموظف (مدين الموظف / دائن العميل) — 0084.';

-- (2) نوع عملية سطر الحجز: عادي / هدية (كمية بلا قيمة) / علاجي (قيد مركّب بموظف).
ALTER TABLE booking_lines ADD COLUMN IF NOT EXISTS operation_type varchar(15) NOT NULL
    DEFAULT 'normal';
ALTER TABLE booking_lines ADD COLUMN IF NOT EXISTS employee_id uuid
    REFERENCES employees(id) ON DELETE SET NULL;
ALTER TABLE booking_lines DROP CONSTRAINT IF EXISTS ck_booking_lines_operation_type;
ALTER TABLE booking_lines ADD CONSTRAINT ck_booking_lines_operation_type
    CHECK (operation_type IN ('normal', 'gift', 'treatment'));
COMMENT ON COLUMN booking_lines.operation_type IS
    'normal = قيمة عادية، gift = بدون قيمة (الكمية تُنفَّذ)، treatment = تنفيذ بقيد مركّب على الموظف.';

-- (3) سلف الموظفين: مستند مستقل يُصرف من المصروفات التشغيلية على حساب مختار.
CREATE TABLE IF NOT EXISTS employee_advances (
    id                 uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    branch_id          uuid NOT NULL REFERENCES branches(id) ON DELETE RESTRICT,
    doc_no             varchar(30),
    doc_date           date NOT NULL,
    employee_id        uuid NOT NULL REFERENCES employees(id) ON DELETE RESTRICT,
    treasury_id        uuid NOT NULL REFERENCES treasuries(id) ON DELETE RESTRICT,
    treasury_method_id uuid REFERENCES treasury_methods(id) ON DELETE RESTRICT,
    charge_account_id  uuid NOT NULL REFERENCES accounts(id) ON DELETE RESTRICT,
    amount             numeric(18,2) NOT NULL CHECK (amount > 0),
    status             varchar(10) NOT NULL DEFAULT 'posted' CHECK (status IN ('draft', 'posted')),
    journal_entry_id   uuid REFERENCES journal_entries(id) ON DELETE SET NULL,
    notes              varchar(500),
    created_by         uuid REFERENCES users(id) ON DELETE SET NULL,
    created_at         timestamptz NOT NULL DEFAULT now(),
    posted_at          timestamptz,
    posted_by          uuid REFERENCES users(id) ON DELETE SET NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_employee_advances_no
    ON employee_advances (branch_id, doc_no) WHERE doc_no IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_employee_advances_branch_date
    ON employee_advances (branch_id, created_at DESC);
CREATE INDEX IF NOT EXISTS ix_employee_advances_employee
    ON employee_advances (employee_id, created_at DESC);
COMMENT ON TABLE employee_advances IS
    'صرف سلفة لموظف من المصروفات التشغيلية: مدين الحساب المُختار (تحميل السلفة) / دائن الخزينة.';

-- (4) تعديلات المراجعة وقيد الفرق العكسي.
CREATE TABLE IF NOT EXISTS review_adjustments (
    id               uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    shift_id         uuid NOT NULL REFERENCES cashier_shifts(id) ON DELETE CASCADE,
    entity_type      varchar(40) NOT NULL,
    entity_id        uuid NOT NULL,
    line_id          uuid,
    field            varchar(40) NOT NULL,
    old_value        text,
    new_value        text,
    diff_amount      numeric(18,2) NOT NULL DEFAULT 0,
    diff_quantity    numeric(18,4) NOT NULL DEFAULT 0,
    note             varchar(500),
    status           varchar(10) NOT NULL DEFAULT 'pending'
                     CHECK (status IN ('pending', 'applied', 'rejected')),
    journal_entry_id uuid REFERENCES journal_entries(id) ON DELETE SET NULL,
    created_by       uuid REFERENCES users(id) ON DELETE SET NULL,
    created_at       timestamptz NOT NULL DEFAULT now(),
    applied_at       timestamptz
);
CREATE INDEX IF NOT EXISTS ix_review_adjustments_shift
    ON review_adjustments (shift_id, status);

CREATE TABLE IF NOT EXISTS review_notes (
    id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    shift_id    uuid NOT NULL REFERENCES cashier_shifts(id) ON DELETE CASCADE,
    entity_type varchar(40) NOT NULL,
    entity_id   uuid NOT NULL,
    note        varchar(500) NOT NULL,
    created_by  uuid REFERENCES users(id) ON DELETE SET NULL,
    created_at  timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_review_notes_entity
    ON review_notes (entity_type, entity_id, created_at DESC);

-- (5) أنواع مصدر جديدة لقيود الأستاذ.
ALTER TABLE journal_entries DROP CONSTRAINT IF EXISTS ck_journal_entries_source_type;
ALTER TABLE journal_entries ADD CONSTRAINT ck_journal_entries_source_type
    CHECK (source_type IN ('manual', 'stock_document', 'stocktake', 'opening', 'invoice',
                           'voucher', 'cash_shift', 'delivery', 'execution',
                           'operating_expense', 'cash_handover', 'service_transfer',
                           'employee_advance', 'review_adjustment'));
