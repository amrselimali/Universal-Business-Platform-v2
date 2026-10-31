-- 0039 — الفلاتر المحفوظة لمركز التقارير
-- لا يُعدَّل أي ترحيل سابق (G1).

CREATE TABLE saved_report_filters (
    id           uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id      uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    report_key   varchar(60)  NOT NULL,
    filter_name  varchar(120) NOT NULL,
    filters      jsonb        NOT NULL DEFAULT '{}'::jsonb,
    created_at   timestamptz  NOT NULL DEFAULT now(),
    updated_at   timestamptz  NOT NULL DEFAULT now(),
    CONSTRAINT ux_saved_report_filters UNIQUE (user_id, report_key, filter_name),
    CONSTRAINT ck_saved_report_filters_name CHECK (length(btrim(filter_name)) BETWEEN 1 AND 120),
    CONSTRAINT ck_saved_report_filters_key CHECK (report_key ~ '^[a-z][a-z0-9_.]{2,59}$'),
    CONSTRAINT ck_saved_report_filters_json CHECK (jsonb_typeof(filters) = 'object')
);

CREATE INDEX ix_saved_report_filters_user ON saved_report_filters (user_id, report_key);

COMMENT ON TABLE saved_report_filters IS
    'فلاتر يحفظها المستخدم لكل تقرير؛ القيد الفريد (user_id, report_key, filter_name) يمنع التكرار.';
