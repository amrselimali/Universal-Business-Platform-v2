-- 0003 — التعريفات الأساسية: الإعدادات، الفروع، المخازن، العملات، الحقول المخصصة
-- ------------------------------------------------------------------------------

CREATE TABLE app_settings (
    id                      uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    company_name            varchar(255) NOT NULL,
    logo_url                varchar(500),
    default_language        varchar(10)  NOT NULL DEFAULT 'ar' CHECK (default_language IN ('ar','en')),
    date_format             varchar(30)  NOT NULL DEFAULT 'dd/MM/yyyy',
    time_format             varchar(30)  NOT NULL DEFAULT 'HH:mm',
    number_format           varchar(30)  NOT NULL DEFAULT '#,##0.00',
    session_timeout_minutes smallint     NOT NULL DEFAULT 30 CHECK (session_timeout_minutes BETWEEN 5 AND 480),
    password_min_length     smallint     NOT NULL DEFAULT 8  CHECK (password_min_length BETWEEN 8 AND 64),
    max_failed_attempts     smallint     NOT NULL DEFAULT 5  CHECK (max_failed_attempts BETWEEN 1 AND 20),
    lockout_minutes         smallint     NOT NULL DEFAULT 15 CHECK (lockout_minutes BETWEEN 1 AND 1440),
    created_at              timestamptz  NOT NULL DEFAULT now(),
    updated_at              timestamptz  NOT NULL DEFAULT now(),
    version                 integer      NOT NULL DEFAULT 1
);

CREATE UNIQUE INDEX ux_app_settings_singleton ON app_settings ((true));

CREATE TABLE branches (
    id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    code          varchar(30)  NOT NULL,
    name_ar       varchar(255) NOT NULL,
    name_en       varchar(255),
    address       varchar(500),
    phone         varchar(50),
    is_active     boolean      NOT NULL DEFAULT true,
    custom_fields jsonb        NOT NULL DEFAULT '{}'::jsonb,
    search_text   text GENERATED ALWAYS AS (
        normalize_search(coalesce(code,'') || ' ' || coalesce(name_ar,'') || ' ' ||
                         coalesce(name_en,'') || ' ' || coalesce(phone,''))
    ) STORED,
    created_at    timestamptz  NOT NULL DEFAULT now(),
    updated_at    timestamptz  NOT NULL DEFAULT now(),
    version       integer      NOT NULL DEFAULT 1,
    CONSTRAINT ux_branches_code UNIQUE (code)
);

CREATE TABLE warehouses (
    id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    code          varchar(30)  NOT NULL,
    name_ar       varchar(255) NOT NULL,
    name_en       varchar(255),
    is_active     boolean      NOT NULL DEFAULT true,
    custom_fields jsonb        NOT NULL DEFAULT '{}'::jsonb,
    search_text   text GENERATED ALWAYS AS (
        normalize_search(coalesce(code,'') || ' ' || coalesce(name_ar,'') || ' ' || coalesce(name_en,''))
    ) STORED,
    created_at    timestamptz  NOT NULL DEFAULT now(),
    updated_at    timestamptz  NOT NULL DEFAULT now(),
    version       integer      NOT NULL DEFAULT 1,
    CONSTRAINT ux_warehouses_code UNIQUE (code)
);

CREATE TABLE warehouse_branches (
    id           uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    warehouse_id uuid NOT NULL REFERENCES warehouses(id) ON DELETE CASCADE,
    branch_id    uuid NOT NULL REFERENCES branches(id) ON DELETE CASCADE,
    created_at   timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ux_warehouse_branches UNIQUE (warehouse_id, branch_id)
);

CREATE TABLE currencies (
    id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    currency_code character(3) NOT NULL,
    name_ar       varchar(100) NOT NULL,
    name_en       varchar(100),
    symbol        varchar(10),
    decimals      smallint     NOT NULL DEFAULT 2 CHECK (decimals BETWEEN 0 AND 6),
    is_base       boolean      NOT NULL DEFAULT false,
    created_at    timestamptz  NOT NULL DEFAULT now(),
    updated_at    timestamptz  NOT NULL DEFAULT now(),
    version       integer      NOT NULL DEFAULT 1,
    CONSTRAINT ux_currencies_code UNIQUE (currency_code),
    CONSTRAINT ux_currencies_name_ar UNIQUE (name_ar),
    CONSTRAINT ux_currencies_name_en UNIQUE (name_en)
);

-- عملة أساسية واحدة فقط
CREATE UNIQUE INDEX ux_currencies_single_base ON currencies (is_base) WHERE is_base;

CREATE TABLE custom_field_definitions (
    id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    entity_name varchar(100) NOT NULL,
    field_key   varchar(60)  NOT NULL CHECK (field_key ~ '^[a-z][a-z0-9_]*$'),
    label_ar    varchar(255) NOT NULL,
    label_en    varchar(255),
    data_type   varchar(20)  NOT NULL CHECK (data_type IN ('text','number','date','bool','list')),
    options     jsonb,
    is_required boolean      NOT NULL DEFAULT false,
    is_active   boolean      NOT NULL DEFAULT true,
    sort_order  integer      NOT NULL DEFAULT 0,
    created_at  timestamptz  NOT NULL DEFAULT now(),
    updated_at  timestamptz  NOT NULL DEFAULT now(),
    version     integer      NOT NULL DEFAULT 1,
    CONSTRAINT ux_custom_fields_entity_key UNIQUE (entity_name, field_key)
);
