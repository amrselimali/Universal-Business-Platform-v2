-- 0015 — محرك الفترات الماليّة الموحد (مخزون/محاسبة/مبيعات/مشتريات/خزائن)
-- ---------------------------------------------------------------------

CREATE TABLE fiscal_periods (
    id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    code          varchar(30)  NOT NULL,
    name_ar       varchar(255) NOT NULL,
    name_en       varchar(255),
    start_date    date         NOT NULL,
    end_date      date         NOT NULL,
    status        varchar(20)  NOT NULL DEFAULT 'OPEN' CHECK (status IN ('OPEN','CLOSED')),
    closed_at     timestamptz,
    closed_by     uuid REFERENCES users(id) ON DELETE SET NULL,
    reopened_at   timestamptz,
    reopened_by   uuid REFERENCES users(id) ON DELETE SET NULL,
    reopen_reason text,
    created_at    timestamptz NOT NULL DEFAULT now(),
    updated_at    timestamptz NOT NULL DEFAULT now(),
    version       integer NOT NULL DEFAULT 1,
    CONSTRAINT ux_fiscal_periods_code UNIQUE (code),
    CONSTRAINT ck_fiscal_periods_range CHECK (end_date >= start_date)
);

CREATE TABLE period_scope_closures (
    id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    period_id     uuid NOT NULL REFERENCES fiscal_periods(id) ON DELETE CASCADE,
    scope_type    varchar(10) NOT NULL CHECK (scope_type IN ('BRANCH','COMPANY')),
    branch_id     uuid REFERENCES branches(id) ON DELETE CASCADE,
    module_code   varchar(30) NOT NULL
                  CHECK (module_code IN ('INVENTORY','ACCOUNTING','SALES','PURCHASES','CASH')),
    status        varchar(20) NOT NULL DEFAULT 'CLOSED' CHECK (status IN ('OPEN','CLOSED')),
    closed_at     timestamptz,
    closed_by     uuid REFERENCES users(id) ON DELETE SET NULL,
    reopened_at   timestamptz,
    reopened_by   uuid REFERENCES users(id) ON DELETE SET NULL,
    reopen_reason text,
    created_at    timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_period_scope CHECK (
        (scope_type = 'COMPANY' AND branch_id IS NULL) OR
        (scope_type = 'BRANCH'  AND branch_id IS NOT NULL)),
    CONSTRAINT ux_period_scope UNIQUE NULLS NOT DISTINCT (period_id, scope_type, branch_id, module_code)
);

CREATE TABLE period_close_checks (
    id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    period_id   uuid NOT NULL REFERENCES fiscal_periods(id) ON DELETE CASCADE,
    scope_type  varchar(10) NOT NULL,
    branch_id   uuid REFERENCES branches(id) ON DELETE SET NULL,
    module_code varchar(30) NOT NULL,
    check_code  varchar(60) NOT NULL,
    status      varchar(20) NOT NULL CHECK (status IN ('PASS','WARNING','BLOCKED')),
    message     text NOT NULL,
    details     jsonb,
    checked_at  timestamptz NOT NULL DEFAULT now(),
    checked_by  uuid REFERENCES users(id) ON DELETE SET NULL
);

CREATE TABLE period_events (
    id            bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    period_id     uuid NOT NULL REFERENCES fiscal_periods(id) ON DELETE CASCADE,
    closure_id    uuid REFERENCES period_scope_closures(id) ON DELETE SET NULL,
    scope_type    varchar(10) NOT NULL,
    branch_id     uuid REFERENCES branches(id) ON DELETE SET NULL,
    module_code   varchar(30) NOT NULL,
    action_code   varchar(30) NOT NULL,
    actor_user_id uuid REFERENCES users(id) ON DELETE SET NULL,
    event_at      timestamptz NOT NULL DEFAULT now(),
    reason        text,
    trace_id      varchar(64),
    details       jsonb
);

CREATE INDEX ix_period_closures_period ON period_scope_closures (period_id, module_code, status);
CREATE INDEX ix_period_closures_branch ON period_scope_closures (branch_id, module_code);
CREATE INDEX ix_period_checks_period ON period_close_checks (period_id, checked_at DESC);
CREATE INDEX ix_period_events_period ON period_events (period_id, event_at DESC);
CREATE INDEX ix_fiscal_periods_range ON fiscal_periods (start_date, end_date);

CREATE TRIGGER trg_touch_fiscal_periods BEFORE UPDATE ON fiscal_periods
FOR EACH ROW EXECUTE FUNCTION fn_touch_version();

-- سجل أحداث الفترات غير قابل للتعديل أو الحذف
CREATE TRIGGER trg_period_events_immutable BEFORE UPDATE OR DELETE ON period_events
FOR EACH ROW EXECUTE FUNCTION fn_audit_log_immutable();

-- هل التاريخ داخل فترة مغلقة (كليًا أو للفرع أو للوحدة)؟
CREATE OR REPLACE FUNCTION fn_period_is_closed(p_date date, p_branch_id uuid, p_module varchar)
RETURNS boolean
LANGUAGE sql
STABLE
AS $$
    SELECT EXISTS (
        SELECT 1 FROM fiscal_periods fp
        WHERE p_date BETWEEN fp.start_date AND fp.end_date
          AND (
              fp.status = 'CLOSED'
              OR EXISTS (
                  SELECT 1 FROM period_scope_closures c
                  WHERE c.period_id = fp.id AND c.module_code = p_module AND c.status = 'CLOSED'
                    AND (c.scope_type = 'COMPANY'
                         OR (c.scope_type = 'BRANCH' AND p_branch_id IS NOT NULL
                             AND c.branch_id = p_branch_id)))
          )
    )
$$;

-- يرفع استثناء PERIOD_CLOSED:CODE إذا كان التاريخ مغلقًا
CREATE OR REPLACE FUNCTION fn_assert_period_open(p_date date, p_branch_id uuid, p_module varchar)
RETURNS void
LANGUAGE plpgsql
AS $$
DECLARE
    v_code varchar;
BEGIN
    SELECT fp.code INTO v_code
    FROM fiscal_periods fp
    WHERE p_date BETWEEN fp.start_date AND fp.end_date
      AND (
          fp.status = 'CLOSED'
          OR EXISTS (
              SELECT 1 FROM period_scope_closures c
              WHERE c.period_id = fp.id AND c.module_code = p_module AND c.status = 'CLOSED'
                AND (c.scope_type = 'COMPANY'
                     OR (c.scope_type = 'BRANCH' AND p_branch_id IS NOT NULL
                         AND c.branch_id = p_branch_id)))
      )
    LIMIT 1;

    IF v_code IS NOT NULL THEN
        RAISE EXCEPTION 'PERIOD_CLOSED:%', v_code USING ERRCODE = '23514';
    END IF;
END;
$$;
