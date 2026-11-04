-- 0072 — مصروفات تشغيلية: مستند مستقل عن المخزون بسطور مرنة.
-- كل سطر: حساب من شجرة الحسابات (يُرحَّل عليه) + بيان + كمية + سعر وحدة + قيمة،
-- ويمكن أن يكون السطر بندًا مخزنيًا (صنف + مخزن + وحدة) فيُنشأ استلام مخزني ويُرحَّل على حساب المخزون.
-- السداد: نقدًا/بنكًا من خزينة وطريقة دفع (سند صرف مرحّل)، أو آجلًا على حساب المورد.

CREATE TABLE IF NOT EXISTS operating_expenses (
    id                 uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    version            integer      NOT NULL DEFAULT 1,
    created_at         timestamptz  NOT NULL DEFAULT now(),
    created_by         uuid REFERENCES users(id) ON DELETE SET NULL,
    updated_at         timestamptz,
    updated_by         uuid REFERENCES users(id) ON DELETE SET NULL,
    branch_id          uuid         NOT NULL REFERENCES branches(id) ON DELETE RESTRICT,
    doc_no             varchar(30),
    doc_date           date         NOT NULL,
    supplier_id        uuid         NOT NULL REFERENCES suppliers(id) ON DELETE RESTRICT,
    treasury_id        uuid REFERENCES treasuries(id) ON DELETE RESTRICT,
    treasury_method_id uuid REFERENCES treasury_methods(id) ON DELETE RESTRICT,
    notes              varchar(500),
    total_amount       numeric(18,2) NOT NULL DEFAULT 0 CHECK (total_amount >= 0),
    status             varchar(10)  NOT NULL DEFAULT 'DRAFT'
                       CHECK (status IN ('DRAFT', 'POSTED')),
    stock_document_id  uuid REFERENCES stock_documents(id) ON DELETE SET NULL,
    voucher_id         uuid REFERENCES vouchers(id) ON DELETE SET NULL,
    journal_entry_id   uuid REFERENCES journal_entries(id) ON DELETE SET NULL,
    posted_at          timestamptz,
    posted_by          uuid REFERENCES users(id) ON DELETE SET NULL,
    CONSTRAINT ck_operating_expense_payment
        CHECK ((treasury_id IS NULL AND treasury_method_id IS NULL)
               OR (treasury_id IS NOT NULL AND treasury_method_id IS NOT NULL))
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_operating_expenses_no
    ON operating_expenses (branch_id, doc_no) WHERE doc_no IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_operating_expenses_branch_date
    ON operating_expenses (branch_id, doc_date DESC);
CREATE INDEX IF NOT EXISTS ix_operating_expenses_supplier
    ON operating_expenses (supplier_id, doc_date DESC);

CREATE TABLE IF NOT EXISTS operating_expense_lines (
    id             uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    expense_id     uuid          NOT NULL REFERENCES operating_expenses(id) ON DELETE CASCADE,
    line_no        integer       NOT NULL CHECK (line_no > 0),
    account_id     uuid          NOT NULL REFERENCES accounts(id) ON DELETE RESTRICT,
    item_id        uuid REFERENCES items(id) ON DELETE RESTRICT,
    unit_id        uuid REFERENCES units(id) ON DELETE RESTRICT,
    warehouse_id   uuid REFERENCES warehouses(id) ON DELETE RESTRICT,
    cost_center_id uuid REFERENCES cost_centers(id) ON DELETE RESTRICT,
    description    varchar(300),
    quantity       numeric(18,4) NOT NULL DEFAULT 1 CHECK (quantity > 0),
    unit_price     numeric(18,4) NOT NULL DEFAULT 0 CHECK (unit_price >= 0),
    amount         numeric(18,2) NOT NULL DEFAULT 0 CHECK (amount >= 0),
    stock_document_id uuid REFERENCES stock_documents(id) ON DELETE SET NULL,
    CONSTRAINT ux_operating_expense_lines UNIQUE (expense_id, line_no),
    CONSTRAINT ck_operating_expense_stock_line CHECK (
        item_id IS NULL
        OR (unit_id IS NOT NULL AND warehouse_id IS NOT NULL))
);

CREATE INDEX IF NOT EXISTS ix_operating_expense_lines_expense
    ON operating_expense_lines (expense_id, line_no);

DROP TRIGGER IF EXISTS trg_touch_operating_expenses ON operating_expenses;
CREATE TRIGGER trg_touch_operating_expenses BEFORE UPDATE ON operating_expenses
FOR EACH ROW EXECUTE FUNCTION fn_touch_version();

-- نوع مصدر جديد لقيد المصروف التشغيلي في يومية الأستاذ.
ALTER TABLE journal_entries DROP CONSTRAINT IF EXISTS ck_journal_entries_source_type;
ALTER TABLE journal_entries ADD CONSTRAINT ck_journal_entries_source_type
    CHECK (source_type IN ('manual', 'stock_document', 'stocktake', 'opening', 'invoice',
                           'voucher', 'cash_shift', 'delivery', 'execution',
                           'operating_expense'));

COMMENT ON TABLE operating_expenses IS
    'مصروفات تشغيلية غير مرتبطة بدورة الحجز: سطور حسابات من الشجرة (+ بنود مخزنية اختيارية) وسداد نقدي/آجل.';
COMMENT ON COLUMN operating_expense_lines.account_id IS
    'الحساب المدين للسطر: مصروف من الشجرة، أو حساب المخزون للسطر المخزني.';
