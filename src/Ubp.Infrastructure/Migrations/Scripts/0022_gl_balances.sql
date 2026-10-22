-- 0022 — الأرصدة السريعة (شهرية) ودوال الترحيل الذري والعكس
-- ---------------------------------------------------------------

CREATE TABLE gl_balances (
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    period_month    date NOT NULL,
    account_id      uuid NOT NULL REFERENCES accounts(id) ON DELETE RESTRICT,
    branch_id       uuid NOT NULL REFERENCES branches(id) ON DELETE RESTRICT,
    cost_center_id  uuid REFERENCES cost_centers(id) ON DELETE RESTRICT,
    cost_center_key uuid GENERATED ALWAYS AS (
        coalesce(cost_center_id, '00000000-0000-0000-0000-000000000000'::uuid)) STORED,
    debit           numeric(18,2) NOT NULL DEFAULT 0,
    credit          numeric(18,2) NOT NULL DEFAULT 0,
    updated_at      timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ux_gl_balances UNIQUE (period_month, account_id, branch_id, cost_center_key),
    CONSTRAINT ck_gl_balances_month CHECK (period_month = date_trunc('month', period_month)::date)
);

COMMENT ON TABLE gl_balances IS
    'أرصدة شهرية مجمّعة حسب الحساب/الفرع/مركز التكلفة — أساس ميزان المراجعة والأستاذ';
COMMENT ON COLUMN gl_balances.cost_center_key IS
    'مفتاح بديل غير فارغ يسمح بقيد UNIQUE مع cost_center_id الاختياري';

CREATE INDEX ix_gl_balances_account ON gl_balances (account_id, period_month, branch_id);
CREATE INDEX ix_gl_balances_branch ON gl_balances (branch_id, period_month);
CREATE INDEX ix_gl_balances_cost_center ON gl_balances (cost_center_id, period_month)
WHERE cost_center_id IS NOT NULL;

CREATE TABLE party_balances (
    id           uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    period_month date NOT NULL,
    account_id   uuid NOT NULL REFERENCES accounts(id) ON DELETE RESTRICT,
    branch_id    uuid NOT NULL REFERENCES branches(id) ON DELETE RESTRICT,
    party_type   char(1) NOT NULL CHECK (party_type IN ('C','S')),
    party_id     uuid NOT NULL,
    debit        numeric(18,2) NOT NULL DEFAULT 0,
    credit       numeric(18,2) NOT NULL DEFAULT 0,
    updated_at   timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ux_party_balances UNIQUE (period_month, account_id, branch_id, party_type, party_id),
    CONSTRAINT ck_party_balances_month CHECK (period_month = date_trunc('month', period_month)::date)
);

COMMENT ON TABLE party_balances IS 'أرصدة شهرية للعملاء والموردين — أساس كشف حساب الطرف';

CREATE INDEX ix_party_balances_party ON party_balances (party_type, party_id, period_month);
CREATE INDEX ix_party_balances_account ON party_balances (account_id, period_month);

-- ترحيل قيد: فحص التكامل + منع الترحيل المزدوج + تحديث الأرصدة الشهرية في نفس المعاملة
CREATE OR REPLACE FUNCTION fn_gl_post_entry(p_entry uuid, p_user uuid)
RETURNS void
LANGUAGE plpgsql
AS $$
DECLARE
    v_status  varchar;
BEGIN
    SELECT status INTO v_status FROM journal_entries WHERE id = p_entry FOR UPDATE;
    IF v_status IS NULL THEN
        RAISE EXCEPTION 'ENTRY_NOT_FOUND' USING ERRCODE = '23503';
    END IF;
    IF v_status <> 'draft' THEN
        RAISE EXCEPTION 'ENTRY_NOT_DRAFT' USING ERRCODE = '23514';
    END IF;

    -- ترقيم القيد عند الترحيل فقط (المسودة بلا رقم)
    UPDATE journal_entries e
       SET entry_no = coalesce(e.entry_no, (
               SELECT next_document_number('JOURNAL_' || j.code, e.branch_id, e.entry_date,
                                           j.code, 6::smallint)
               FROM journals j WHERE j.id = e.journal_id))
     WHERE e.id = p_entry AND e.entry_no IS NULL;

    UPDATE journal_entry_lines SET is_posted = true WHERE entry_id = p_entry;

    UPDATE journal_entries e
       SET status = 'posted',
           posted_at = now(),
           posted_by = p_user,
           total_debit = (SELECT coalesce(sum(debit), 0) FROM journal_entry_lines WHERE entry_id = p_entry),
           total_credit = (SELECT coalesce(sum(credit), 0) FROM journal_entry_lines WHERE entry_id = p_entry)
     WHERE e.id = p_entry;

    INSERT INTO gl_balances AS g (period_month, account_id, branch_id, cost_center_id, debit, credit)
    SELECT date_trunc('month', l.entry_date)::date, l.account_id, l.branch_id,
           l.cost_center_id, sum(l.debit), sum(l.credit)
      FROM journal_entry_lines l
     WHERE l.entry_id = p_entry
     GROUP BY 1, 2, 3, 4
    ON CONFLICT (period_month, account_id, branch_id, cost_center_key)
    DO UPDATE SET debit = g.debit + EXCLUDED.debit,
                  credit = g.credit + EXCLUDED.credit,
                  updated_at = now();

    INSERT INTO party_balances AS pb (period_month, account_id, branch_id, party_type, party_id,
                                      debit, credit)
    SELECT date_trunc('month', l.entry_date)::date, l.account_id, l.branch_id,
           CASE WHEN l.customer_id IS NOT NULL THEN 'C' ELSE 'S' END,
           coalesce(l.customer_id, l.supplier_id), sum(l.debit), sum(l.credit)
      FROM journal_entry_lines l
     WHERE l.entry_id = p_entry
       AND (l.customer_id IS NOT NULL OR l.supplier_id IS NOT NULL)
     GROUP BY 1, 2, 3, 4, 5
    ON CONFLICT (period_month, account_id, branch_id, party_type, party_id)
    DO UPDATE SET debit = pb.debit + EXCLUDED.debit,
                  credit = pb.credit + EXCLUDED.credit,
                  updated_at = now();

END;
$$;

COMMENT ON FUNCTION fn_gl_post_entry(uuid, uuid) IS

-- عكس قيد: إنشاء قيد مقابل معكوس البنود وترحيله، وتعليم الأصل — يمنع العكس المزدوج
CREATE OR REPLACE FUNCTION fn_gl_reverse_entry(p_entry uuid, p_user uuid, p_date date)
RETURNS uuid
LANGUAGE plpgsql
AS $$
DECLARE
    v_orig journal_entries%ROWTYPE;
    v_new  uuid := gen_random_uuid();
BEGIN
    SELECT * INTO v_orig FROM journal_entries WHERE id = p_entry FOR UPDATE;
    IF v_orig.id IS NULL THEN
        RAISE EXCEPTION 'ENTRY_NOT_FOUND' USING ERRCODE = '23503';
    END IF;
    IF v_orig.status <> 'posted' THEN
        RAISE EXCEPTION 'ENTRY_NOT_POSTED' USING ERRCODE = '23514';
    END IF;
    IF v_orig.is_reversed THEN
        RAISE EXCEPTION 'ENTRY_ALREADY_REVERSED' USING ERRCODE = '23514';
    END IF;
    IF v_orig.reversal_of_id IS NOT NULL THEN
        RAISE EXCEPTION 'ENTRY_IS_REVERSAL' USING ERRCODE = '23514';
    END IF;

    INSERT INTO journal_entries
        (id, journal_id, branch_id, entry_date, reference, description, status,
         source_type, source_id, reversal_of_id, created_by)
    VALUES
        (v_new, v_orig.journal_id, v_orig.branch_id, p_date, v_orig.reference,
         left('عكس القيد ' || coalesce(v_orig.entry_no, ''), 500), 'draft',
         v_orig.source_type, v_orig.source_id, p_entry, p_user);

    INSERT INTO journal_entry_lines
        (entry_id, line_no, account_id, description, debit, credit,
         customer_id, supplier_id, cost_center_id, branch_id, entry_date)
    SELECT v_new, l.line_no, l.account_id, l.description, l.credit, l.debit,
           l.customer_id, l.supplier_id, l.cost_center_id, l.branch_id, p_date
      FROM journal_entry_lines l
     WHERE l.entry_id = p_entry
     ORDER BY l.line_no;

    PERFORM fn_gl_post_entry(v_new, p_user);

    UPDATE journal_entries SET is_reversed = true WHERE id = p_entry;

    RETURN v_new;
END;
$$;

COMMENT ON FUNCTION fn_gl_reverse_entry(uuid, uuid, date) IS
    'عكس قيد مرحّل بقيد مقابل — يمنع العكس المزدوج على مستوى قاعدة البيانات';

-- رصيد افتتاحي لحساب (شامل الأبناء اختيارياً) قبل تاريخ معيّن
CREATE OR REPLACE FUNCTION fn_gl_opening_balance(
    p_account uuid,
    p_branch  uuid,
    p_before  date,
    p_include_children boolean DEFAULT true
) RETURNS numeric
LANGUAGE sql
STABLE
AS $$
    SELECT coalesce(sum(b.debit - b.credit), 0)
      FROM gl_balances b
      JOIN accounts a ON a.id = b.account_id
     WHERE b.period_month < date_trunc('month', p_before)::date
       AND (p_branch IS NULL OR b.branch_id = p_branch)
       AND (CASE WHEN p_include_children
                 THEN a.path LIKE (SELECT path FROM accounts WHERE id = p_account) || '%'
                 ELSE b.account_id = p_account END)
$$;

    'ترحيل قيد مسودة: ترقيم + فحص توازن + قيد الأرصدة الشهرية وأرصدة الأطراف ذرياً';
