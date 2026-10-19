-- 0005 — سجل الأنشطة + جداول مركز الجودة (QA)
-- -------------------------------------------------

CREATE TABLE audit_log (
    id         bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    user_id    uuid REFERENCES users(id) ON DELETE SET NULL,
    branch_id  uuid REFERENCES branches(id) ON DELETE SET NULL,
    table_name varchar(100) NOT NULL,
    record_id  varchar(100) NOT NULL,
    action     varchar(20)  NOT NULL CHECK (action IN
        ('INSERT','UPDATE','DELETE','LOGIN','LOGOUT','LOGIN_FAILED',
         'PERMISSION_CHANGE','PASSWORD_RESET','MIGRATION')),
    old_data   jsonb,
    new_data   jsonb,
    ip_address inet,
    trace_id   varchar(64),
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE qa_test_definitions (
    id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    code        varchar(120) NOT NULL,
    name_ar     varchar(255) NOT NULL,
    name_en     varchar(255),
    category    varchar(50)  NOT NULL,
    criticality varchar(20)  NOT NULL DEFAULT 'medium' CHECK (criticality IN ('low','medium','high','critical')),
    is_active   boolean      NOT NULL DEFAULT true,
    created_at  timestamptz  NOT NULL DEFAULT now(),
    updated_at  timestamptz  NOT NULL DEFAULT now(),
    version     integer      NOT NULL DEFAULT 1,
    CONSTRAINT ux_qa_test_code UNIQUE (code)
);

CREATE TABLE qa_test_runs (
    id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    requested_by  uuid REFERENCES users(id) ON DELETE SET NULL,
    environment   varchar(30) NOT NULL,
    code_revision varchar(100),
    status        varchar(20) NOT NULL CHECK (status IN ('Queued','Running','Completed','Failed','Blocked','Cancelled')),
    total_count   integer NOT NULL DEFAULT 0,
    passed_count  integer NOT NULL DEFAULT 0,
    failed_count  integer NOT NULL DEFAULT 0,
    trace_id      varchar(64),
    started_at    timestamptz NOT NULL DEFAULT now(),
    finished_at   timestamptz,
    created_at    timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE qa_test_results (
    id          bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    run_id      uuid NOT NULL REFERENCES qa_test_runs(id) ON DELETE CASCADE,
    test_code   varchar(120) NOT NULL,
    attempt     smallint NOT NULL DEFAULT 1,
    status      varchar(20) NOT NULL CHECK (status IN ('Passed','Failed','Skipped','Error','Blocked','Stale')),
    duration_ms integer NOT NULL DEFAULT 0,
    message     text,
    trace_id    varchar(64),
    created_at  timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE qa_test_artifacts (
    id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    run_id     uuid NOT NULL REFERENCES qa_test_runs(id) ON DELETE CASCADE,
    test_code  varchar(120),
    kind       varchar(30) NOT NULL,
    reference  varchar(1000) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);
