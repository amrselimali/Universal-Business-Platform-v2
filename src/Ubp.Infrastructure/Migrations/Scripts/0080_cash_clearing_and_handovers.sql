-- 0080 — حساب وسيط لنقدية الفرع + مستند تسليم نقدية من الخزينة إلى الحساب الوسيط
-- ---------------------------------------------------------------------------
-- 1) علامة على حساب الشجرة: «حساب وسيط لنقدية الفرع» تُستخدم في تسليم النقدية.
-- 2) cash_handovers: مستند تسليم نقدية يخفض الخزينة ويزيد الحساب الوسيط ويظهر
--    ضمن مصروفات الوردية.

ALTER TABLE accounts ADD COLUMN IF NOT EXISTS is_cash_clearing boolean NOT NULL DEFAULT false;
COMMENT ON COLUMN accounts.is_cash_clearing IS
    'حساب وسيط لنقدية الفرع: يقبل تسليم النقدية من الخزائن (0080)';

CREATE TABLE IF NOT EXISTS cash_handovers (
    id                 uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    branch_id          uuid NOT NULL REFERENCES branches(id) ON DELETE RESTRICT,
    treasury_id        uuid NOT NULL REFERENCES treasuries(id) ON DELETE RESTRICT,
    clearing_account_id uuid NOT NULL REFERENCES accounts(id) ON DELETE RESTRICT,
    amount             numeric(18,2) NOT NULL CHECK (amount > 0),
    doc_date           date NOT NULL,
    notes              varchar(500),
    journal_entry_id   uuid REFERENCES journal_entries(id) ON DELETE SET NULL,
    created_at         timestamptz NOT NULL DEFAULT now(),
    created_by         uuid REFERENCES users(id) ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS ix_cash_handovers_branch_date
    ON cash_handovers (branch_id, doc_date DESC);

COMMENT ON TABLE cash_handovers IS
    'تسليم نقدية من خزينة إلى حساب وسيط لنقدية الفرع: يخفض الخزينة ويزيد الوسيط ويظهر في مصروفات الوردية.';

-- نوع مصدر جديد لقيد تسليم النقدية
ALTER TABLE journal_entries DROP CONSTRAINT IF EXISTS ck_journal_entries_source_type;
ALTER TABLE journal_entries ADD CONSTRAINT ck_journal_entries_source_type
    CHECK (source_type IN ('manual', 'stock_document', 'stocktake', 'opening', 'invoice',
                           'voucher', 'cash_shift', 'delivery', 'execution',
                           'operating_expense', 'cash_handover'));
