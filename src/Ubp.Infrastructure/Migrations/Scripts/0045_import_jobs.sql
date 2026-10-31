-- 0045 — سجل عمليات الاستيراد (معاينة/تنفيذ) للأرصدة الافتتاحية وغيرها
-- لا يُعدَّل أي ترحيل سابق (G1).

CREATE TABLE import_jobs (
    id             uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    kind           varchar(30) NOT NULL
                   CHECK (kind IN ('OPENING_BALANCES', 'ITEMS', 'PARTNERS')),
    file_name      varchar(255),
    status         varchar(20) NOT NULL DEFAULT 'PREVIEWED'
                   CHECK (status IN ('PREVIEWED', 'IMPORTED', 'FAILED')),
    rows_total     integer NOT NULL DEFAULT 0 CHECK (rows_total >= 0),
    rows_imported  integer NOT NULL DEFAULT 0 CHECK (rows_imported >= 0),
    rows_failed    integer NOT NULL DEFAULT 0 CHECK (rows_failed >= 0),
    total_debit    numeric(18,4) NOT NULL DEFAULT 0,
    total_credit   numeric(18,4) NOT NULL DEFAULT 0,
    error_summary  text,
    created_by     uuid REFERENCES users(id) ON DELETE SET NULL,
    created_at     timestamptz NOT NULL DEFAULT now(),
    finished_at    timestamptz,
    CONSTRAINT ck_import_jobs_counts CHECK (rows_imported + rows_failed <= rows_total)
);

CREATE INDEX ix_import_jobs_created ON import_jobs (created_at DESC);

COMMENT ON TABLE import_jobs IS
    'سجل عمليات الاستيراد: المعاينة لا تكتب بيانات، والتنفيذ يكتب داخل معاملة واحدة ثم يُسجَّل هنا.';
