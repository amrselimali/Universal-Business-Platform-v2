-- 0041 — إجماليات المبيعات اليومية للوحة المؤشرات
-- تُحدَّث ذرياً داخل معاملة ترحيل الفاتورة/المرتجع (Atomic) لتُغني اللوحة عن تجميع الفواتير.

CREATE TABLE sales_daily_totals (
    day            date    NOT NULL,
    branch_id      uuid    NOT NULL REFERENCES branches(id) ON DELETE RESTRICT,
    invoice_count  integer NOT NULL DEFAULT 0 CHECK (invoice_count >= 0),
    return_count   integer NOT NULL DEFAULT 0 CHECK (return_count >= 0),
    net_total      numeric(18,4) NOT NULL DEFAULT 0,
    tax_total      numeric(18,4) NOT NULL DEFAULT 0,
    gross_total    numeric(18,4) NOT NULL DEFAULT 0,
    cogs_total     numeric(18,4) NOT NULL DEFAULT 0,
    updated_at     timestamptz   NOT NULL DEFAULT now(),
    CONSTRAINT pk_sales_daily_totals PRIMARY KEY (day, branch_id)
);

CREATE INDEX ix_sales_daily_totals_day ON sales_daily_totals (day);

COMMENT ON TABLE sales_daily_totals IS
    'إجماليات يومية لكل فرع؛ تُحدَّث في نفس معاملة ترحيل الفاتورة حتى تظل اللوحة فورية.';
