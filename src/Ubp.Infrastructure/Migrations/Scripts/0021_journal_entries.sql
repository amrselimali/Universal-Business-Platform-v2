-- 0021 — قيود اليومية: الرأس والبنود، حماية القيود المرحّلة، وفحص التوازن عند الترحيل
-- ---------------------------------------------------------------------------------

CREATE TABLE journal_entries (
    id             uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    journal_id     uuid NOT NULL REFERENCES journals(id) ON DELETE RESTRICT,
    branch_id      uuid NOT NULL REFERENCES branches(id) ON DELETE RESTRICT,
    entry_no       varchar(30),
    entry_date     date NOT NULL,
    reference      varchar(100),
    description    varchar(500),
    status         varchar(10) NOT NULL DEFAULT 'draft'
                   CHECK (status IN ('draft','posted','cancelled')),
    source_type    varchar(30) NOT NULL DEFAULT 'manual'
                   CHECK (source_type IN ('manual','stock_document','stocktake','opening','invoice')),
    source_id      uuid,
    reversal_of_id uuid REFERENCES journal_entries(id) ON DELETE RESTRICT,
    is_reversed    boolean NOT NULL DEFAULT false,
    total_debit    numeric(18,2) NOT NULL DEFAULT 0 CHECK (total_debit >= 0),
    total_credit   numeric(18,2) NOT NULL DEFAULT 0 CHECK (total_credit >= 0),
    posted_at      timestamptz,
    posted_by      uuid REFERENCES users(id) ON DELETE SET NULL,
    created_at     timestamptz NOT NULL DEFAULT now(),
    created_by     uuid REFERENCES users(id) ON DELETE SET NULL,
    updated_at     timestamptz,
    updated_by     uuid REFERENCES users(id) ON DELETE SET NULL,
    version        integer NOT NULL DEFAULT 1,
    CONSTRAINT ck_journal_entries_balanced CHECK (
        status <> 'posted' OR total_debit = total_credit),
    CONSTRAINT ck_journal_entries_reversal CHECK (
        reversal_of_id IS NULL OR reversal_of_id <> id)
);

COMMENT ON COLUMN journal_entries.source_type IS
    'مصدر القيد: يدوي أو مستند مخزون/جرد/افتتاحي — يمنع التوليد المزدوج';
COMMENT ON COLUMN journal_entries.is_reversed IS
    'يُعلَّم على القيد الأصلي بعد إنشاء قيد العكس (منع العكس المزدوج)';

CREATE UNIQUE INDEX ux_journal_entries_no ON journal_entries (branch_id, entry_no)
WHERE entry_no IS NOT NULL;
CREATE UNIQUE INDEX ux_journal_entries_reversal ON journal_entries (reversal_of_id)
WHERE reversal_of_id IS NOT NULL;
CREATE UNIQUE INDEX ux_journal_entries_source ON journal_entries (source_type, source_id)
WHERE source_id IS NOT NULL AND reversal_of_id IS NULL AND status <> 'cancelled';

CREATE INDEX ix_journal_entries_branch_date ON journal_entries (branch_id, entry_date DESC, id DESC);
CREATE INDEX ix_journal_entries_status ON journal_entries (status, entry_date DESC);
CREATE INDEX ix_journal_entries_journal ON journal_entries (journal_id, status, entry_date DESC);
CREATE INDEX ix_journal_entries_source ON journal_entries (source_type, source_id);

CREATE TABLE journal_entry_lines (
    id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    created_at    timestamptz NOT NULL DEFAULT now(),
    entry_id      uuid NOT NULL REFERENCES journal_entries(id) ON DELETE CASCADE,
    line_no       integer NOT NULL CHECK (line_no > 0),
    account_id    uuid NOT NULL REFERENCES accounts(id) ON DELETE RESTRICT,
    description   varchar(300),
    debit         numeric(18,2) NOT NULL DEFAULT 0,
    credit        numeric(18,2) NOT NULL DEFAULT 0,
    customer_id   uuid REFERENCES customers(id) ON DELETE RESTRICT,
    supplier_id   uuid REFERENCES suppliers(id) ON DELETE RESTRICT,
    cost_center_id uuid REFERENCES cost_centers(id) ON DELETE RESTRICT,
    branch_id     uuid NOT NULL REFERENCES branches(id) ON DELETE RESTRICT,
    entry_date    date NOT NULL,
    is_posted     boolean NOT NULL DEFAULT false,
    CONSTRAINT ux_journal_entry_lines UNIQUE (entry_id, line_no),
    CONSTRAINT ck_journal_line_amounts CHECK (
        debit >= 0 AND credit >= 0 AND (debit = 0 OR credit = 0) AND debit + credit > 0),
    CONSTRAINT ck_journal_line_party CHECK (customer_id IS NULL OR supplier_id IS NULL)
);

COMMENT ON COLUMN journal_entry_lines.entry_date IS
    'منسوخ من الرأس لتسريع كشوف الأستاذ و keyset pagination بدون ربط';
COMMENT ON COLUMN journal_entry_lines.branch_id IS
    'منسوخ من الرأس لعزل الفروع على مستوى البند';

CREATE INDEX ix_journal_lines_ledger ON journal_entry_lines (account_id, entry_date DESC, id DESC)
WHERE is_posted;
CREATE INDEX ix_journal_lines_party_customer ON journal_entry_lines (customer_id, entry_date DESC)
WHERE customer_id IS NOT NULL AND is_posted;
CREATE INDEX ix_journal_lines_party_supplier ON journal_entry_lines (supplier_id, entry_date DESC)
WHERE supplier_id IS NOT NULL AND is_posted;
CREATE INDEX ix_journal_lines_cost_center ON journal_entry_lines (cost_center_id, entry_date DESC)
WHERE cost_center_id IS NOT NULL;
CREATE INDEX ix_journal_lines_entry_date ON journal_entry_lines (branch_id, entry_date);

CREATE TRIGGER trg_touch_journal_entries BEFORE UPDATE ON journal_entries
FOR EACH ROW EXECUTE FUNCTION fn_touch_version();


-- القيد المسودة فقط قابل للتعديل أو الحذف. المرحّل/الملغي: لا يتغير منه سوى علم العكس.
CREATE OR REPLACE FUNCTION fn_journal_entry_guard()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        IF OLD.status <> 'draft' THEN
            RAISE EXCEPTION 'JOURNAL_ENTRY_IMMUTABLE' USING ERRCODE = '23514';
        END IF;
        RETURN OLD;
    END IF;

    IF OLD.status = 'draft' THEN
        RETURN NEW;
    END IF;

    -- بعد الترحيل: التغيير الوحيد المسموح هو تعليم الأصل كمُعكوس
    IF NEW.is_reversed IS DISTINCT FROM OLD.is_reversed
       AND NEW.status = OLD.status
       AND NEW.journal_id = OLD.journal_id
       AND NEW.branch_id = OLD.branch_id
       AND NEW.entry_no IS NOT DISTINCT FROM OLD.entry_no
       AND NEW.entry_date = OLD.entry_date
       AND NEW.reference IS NOT DISTINCT FROM OLD.reference
       AND NEW.description IS NOT DISTINCT FROM OLD.description
       AND NEW.source_type = OLD.source_type
       AND NEW.source_id IS NOT DISTINCT FROM OLD.source_id
       AND NEW.reversal_of_id IS NOT DISTINCT FROM OLD.reversal_of_id
       AND NEW.total_debit = OLD.total_debit
       AND NEW.total_credit = OLD.total_credit
       AND NEW.posted_at IS NOT DISTINCT FROM OLD.posted_at
       AND NEW.posted_by IS NOT DISTINCT FROM OLD.posted_by THEN
        RETURN NEW;
    END IF;

    RAISE EXCEPTION 'JOURNAL_ENTRY_IMMUTABLE' USING ERRCODE = '23514';
END;
$$;

CREATE TRIGGER trg_journal_entry_guard BEFORE UPDATE OR DELETE ON journal_entries
FOR EACH ROW EXECUTE FUNCTION fn_journal_entry_guard();

-- منع تعديل أو حذف بند داخل قيد غير مسودة
CREATE OR REPLACE FUNCTION fn_journal_line_guard()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_status varchar;
BEGIN
    IF TG_OP = 'DELETE' AND NOT EXISTS (SELECT 1 FROM journal_entries WHERE id = OLD.entry_id) THEN
        RETURN OLD;
    END IF;

    SELECT status INTO v_status FROM journal_entries
    WHERE id = CASE WHEN TG_OP = 'DELETE' THEN OLD.entry_id ELSE NEW.entry_id END;

    IF v_status IS DISTINCT FROM 'draft' THEN
        RAISE EXCEPTION 'JOURNAL_ENTRY_IMMUTABLE' USING ERRCODE = '23514';
    END IF;
    RETURN CASE WHEN TG_OP = 'DELETE' THEN OLD ELSE NEW END;
END;
$$;

CREATE TRIGGER trg_journal_line_guard BEFORE INSERT OR UPDATE OR DELETE ON journal_entry_lines
FOR EACH ROW EXECUTE FUNCTION fn_journal_line_guard();


-- فحص شامل لحظة الترحيل: التوازن، الفترة، الحسابات، الأطراف، مراكز التكلفة
CREATE OR REPLACE FUNCTION fn_journal_post_integrity()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_debit  numeric;
    v_credit numeric;
    v_lines  integer;
    v_bad    record;
BEGIN
    IF NOT (NEW.status = 'posted' AND OLD.status <> 'posted') THEN
        RETURN NEW;
    END IF;

    SELECT coalesce(sum(debit), 0), coalesce(sum(credit), 0), count(*)
      INTO v_debit, v_credit, v_lines
      FROM journal_entry_lines WHERE entry_id = NEW.id;

    IF v_lines = 0 OR v_debit = 0 OR v_debit <> v_credit THEN
        RAISE EXCEPTION 'ENTRY_NOT_BALANCED' USING ERRCODE = '23514';
    END IF;

    IF NEW.total_debit <> v_debit OR NEW.total_credit <> v_credit THEN
        RAISE EXCEPTION 'ENTRY_TOTALS_MISMATCH' USING ERRCODE = '23514';
    END IF;

    PERFORM fn_assert_period_open(NEW.entry_date, NEW.branch_id, 'ACCOUNTING');

    FOR v_bad IN
        SELECT l.line_no, a.is_group, a.is_active, a.account_type,
               a.cost_center_policy, l.customer_id, l.supplier_id, l.cost_center_id
        FROM journal_entry_lines l
        JOIN accounts a ON a.id = l.account_id
        WHERE l.entry_id = NEW.id
          AND (a.is_group
               OR NOT a.is_active
               OR (a.account_type = 'asset_receivable' AND l.customer_id IS NULL)
               OR (a.account_type = 'liability_payable' AND l.supplier_id IS NULL)
               OR (a.cost_center_policy = 2 AND l.cost_center_id IS NULL)
               OR (a.cost_center_policy = 0 AND l.cost_center_id IS NOT NULL))
        ORDER BY l.line_no
        LIMIT 1
    LOOP
        IF v_bad.is_group THEN
            RAISE EXCEPTION 'ACCOUNT_IS_GROUP:%', v_bad.line_no USING ERRCODE = '23514';
        END IF;
        IF NOT v_bad.is_active THEN
            RAISE EXCEPTION 'ACCOUNT_INACTIVE:%', v_bad.line_no USING ERRCODE = '23514';
        END IF;
        IF v_bad.account_type = 'asset_receivable' AND v_bad.customer_id IS NULL THEN
            RAISE EXCEPTION 'PARTY_REQUIRED:%', v_bad.line_no USING ERRCODE = '23514';
        END IF;
        IF v_bad.account_type = 'liability_payable' AND v_bad.supplier_id IS NULL THEN
            RAISE EXCEPTION 'PARTY_REQUIRED:%', v_bad.line_no USING ERRCODE = '23514';
        END IF;
        IF v_bad.cost_center_policy = 2 AND v_bad.cost_center_id IS NULL THEN
            RAISE EXCEPTION 'COST_CENTER_REQUIRED:%', v_bad.line_no USING ERRCODE = '23514';
        END IF;
        IF v_bad.cost_center_policy = 0 AND v_bad.cost_center_id IS NOT NULL THEN
            RAISE EXCEPTION 'COST_CENTER_NOT_ALLOWED:%', v_bad.line_no USING ERRCODE = '23514';
        END IF;
    END LOOP;

    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_journal_post_integrity BEFORE UPDATE ON journal_entries
FOR EACH ROW EXECUTE FUNCTION fn_journal_post_integrity();
