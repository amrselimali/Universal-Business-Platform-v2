-- 0049 — الأصول الثابتة + العدادات + الصيانة + الإهلاك (8أ)
-- لا يُعدَّل أي ترحيل سابق (G1).

CREATE TABLE asset_categories (
    id                             uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    code                           varchar(30)  NOT NULL UNIQUE,
    name_ar                        varchar(255) NOT NULL,
    name_en                        varchar(255),
    depreciation_method            varchar(20)  NOT NULL DEFAULT 'LINEAR'
                                   CHECK (depreciation_method IN ('LINEAR', 'UNITS')),
    depreciation_frequency         varchar(10)  NOT NULL DEFAULT 'MONTHLY'
                                   CHECK (depreciation_frequency IN ('MONTHLY', 'ANNUAL')),
    useful_life_months             integer      CHECK (useful_life_months > 0),
    is_metered                     boolean      NOT NULL DEFAULT false,
    asset_account_id               uuid REFERENCES accounts(id) ON DELETE RESTRICT,
    accum_account_id               uuid REFERENCES accounts(id) ON DELETE RESTRICT,
    expense_account_id             uuid REFERENCES accounts(id) ON DELETE RESTRICT,
    maintenance_expense_account_id uuid REFERENCES accounts(id) ON DELETE RESTRICT,
    is_active                      boolean      NOT NULL DEFAULT true,
    created_at                     timestamptz  NOT NULL DEFAULT now(),
    updated_at                     timestamptz  NOT NULL DEFAULT now(),
    version                        integer      NOT NULL DEFAULT 1,
    CONSTRAINT ck_asset_categories_life
        CHECK (depreciation_method <> 'LINEAR' OR useful_life_months > 0)
);

CREATE TABLE fixed_assets (
    id                          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    code                        varchar(40)  NOT NULL UNIQUE,
    name_ar                     varchar(255) NOT NULL,
    category_id                 uuid NOT NULL REFERENCES asset_categories(id) ON DELETE RESTRICT,
    branch_id                   uuid REFERENCES branches(id) ON DELETE RESTRICT,
    vendor_id                   uuid REFERENCES suppliers(id) ON DELETE SET NULL,
    purchase_date               date NOT NULL,
    in_service_date             date,
    cost                        numeric(18,2) NOT NULL CHECK (cost >= 0),
    salvage_value               numeric(18,2) NOT NULL DEFAULT 0 CHECK (salvage_value >= 0),
    status                      varchar(20)  NOT NULL DEFAULT 'ACTIVE'
                                CHECK (status IN ('ACTIVE', 'IDLE', 'DISPOSED')),
    is_metered                  boolean      NOT NULL DEFAULT false,
    meter_type                  varchar(20)
                                CHECK (meter_type IN ('PAGES', 'COPIES', 'HOURS', 'UNITS')),
    current_meter_value         numeric(18,2) NOT NULL DEFAULT 0 CHECK (current_meter_value >= 0),
    meter_resets_on_maintenance boolean      NOT NULL DEFAULT false,
    disposed_at                 timestamptz,
    disposal_amount             numeric(18,2),
    journal_entry_id            uuid REFERENCES journal_entries(id) ON DELETE SET NULL,
    created_at                  timestamptz  NOT NULL DEFAULT now(),
    created_by                  uuid REFERENCES users(id) ON DELETE SET NULL,
    updated_at                  timestamptz  NOT NULL DEFAULT now(),
    updated_by                  uuid REFERENCES users(id) ON DELETE SET NULL,
    version                     integer      NOT NULL DEFAULT 1,
    CONSTRAINT ck_fixed_assets_salvage CHECK (salvage_value <= cost),
    CONSTRAINT ck_fixed_assets_meter CHECK (NOT is_metered OR meter_type IS NOT NULL),
    CONSTRAINT ck_fixed_assets_disposal CHECK (status <> 'DISPOSED' OR disposed_at IS NOT NULL)
);

CREATE INDEX ix_fixed_assets_branch ON fixed_assets (branch_id, status);
CREATE INDEX ix_fixed_assets_category ON fixed_assets (category_id);

COMMENT ON COLUMN fixed_assets.branch_id IS
    'الفرع المالك؛ NULL = أصل مشترك بين الفروع (قرار 8أ).';

CREATE TABLE asset_meter_readings (
    id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    asset_id   uuid NOT NULL REFERENCES fixed_assets(id) ON DELETE CASCADE,
    reading_at timestamptz NOT NULL DEFAULT now(),
    value      numeric(18,2) NOT NULL CHECK (value >= 0),
    delta      numeric(18,2) NOT NULL DEFAULT 0,
    source     varchar(20) NOT NULL DEFAULT 'MANUAL'
               CHECK (source IN ('MANUAL', 'SALES', 'MAINTENANCE_RESET')),
    note       varchar(300),
    created_by uuid REFERENCES users(id) ON DELETE SET NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_asset_meter_readings_asset ON asset_meter_readings (asset_id, reading_at DESC);

CREATE TABLE asset_maintenance_plans (
    id             uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    asset_id       uuid NOT NULL REFERENCES fixed_assets(id) ON DELETE CASCADE,
    kind           varchar(10) NOT NULL CHECK (kind IN ('TIME', 'METER')),
    interval_days  integer CHECK (interval_days > 0),
    interval_units numeric(18,2) CHECK (interval_units > 0),
    next_due_date  date,
    next_due_meter numeric(18,2),
    is_active      boolean NOT NULL DEFAULT true,
    created_at     timestamptz NOT NULL DEFAULT now(),
    updated_at     timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_asset_plans_interval CHECK (
        (kind = 'TIME' AND interval_days IS NOT NULL) OR
        (kind = 'METER' AND interval_units IS NOT NULL))
);

CREATE TABLE asset_maintenance_orders (
    id                uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    asset_id          uuid NOT NULL REFERENCES fixed_assets(id) ON DELETE RESTRICT,
    plan_id           uuid REFERENCES asset_maintenance_plans(id) ON DELETE SET NULL,
    maintenance_date  date NOT NULL,
    cost              numeric(18,2) NOT NULL DEFAULT 0 CHECK (cost >= 0),
    vendor_id         uuid REFERENCES suppliers(id) ON DELETE SET NULL,
    action            varchar(20) NOT NULL DEFAULT 'PREVENTIVE'
                      CHECK (action IN ('PREVENTIVE', 'CORRECTIVE')),
    status            varchar(20) NOT NULL DEFAULT 'OPEN'
                      CHECK (status IN ('OPEN', 'DONE', 'CANCELLED')),
    reset_meter_value numeric(18,2),
    notes             varchar(500),
    journal_entry_id  uuid REFERENCES journal_entries(id) ON DELETE SET NULL,
    opened_at         timestamptz NOT NULL DEFAULT now(),
    closed_at         timestamptz,
    created_by        uuid REFERENCES users(id) ON DELETE SET NULL,
    CONSTRAINT ck_asset_orders_closed CHECK (status <> 'DONE' OR closed_at IS NOT NULL)
);

CREATE INDEX ix_asset_orders_asset
    ON asset_maintenance_orders (asset_id, maintenance_date DESC);

CREATE TABLE asset_depreciation_runs (
    id               uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    period_month     date NOT NULL,
    branch_id        uuid REFERENCES branches(id) ON DELETE RESTRICT,
    total_amount     numeric(18,2) NOT NULL DEFAULT 0,
    journal_entry_id uuid REFERENCES journal_entries(id) ON DELETE SET NULL,
    run_by           uuid REFERENCES users(id) ON DELETE SET NULL,
    run_at           timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ux_asset_dep_runs UNIQUE NULLS NOT DISTINCT (period_month, branch_id),
    CONSTRAINT ck_asset_dep_runs_month
        CHECK (period_month = date_trunc('month', period_month)::date)
);

CREATE TABLE asset_depreciation_lines (
    id                uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    run_id            uuid NOT NULL REFERENCES asset_depreciation_runs(id) ON DELETE CASCADE,
    asset_id          uuid NOT NULL REFERENCES fixed_assets(id) ON DELETE RESTRICT,
    period_month      date NOT NULL,
    amount            numeric(18,2) NOT NULL CHECK (amount >= 0),
    accumulated_after numeric(18,2) NOT NULL CHECK (accumulated_after >= 0),
    CONSTRAINT ux_asset_dep_lines UNIQUE (asset_id, period_month)
);

COMMENT ON TABLE asset_depreciation_lines IS
    'قيد فريد (asset_id, period_month) يمنع إهلاك الأصل مرتين في نفس الشهر.';

