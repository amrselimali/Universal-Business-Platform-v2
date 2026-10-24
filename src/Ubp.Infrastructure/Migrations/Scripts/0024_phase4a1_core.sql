-- 0024 — المرحلة 4A-1: التأسيس (إعدادات مُصنّفة، أنواع مفاتيح الترحيل، الموظفون،
-- الضرائب، شروط الدفع، قوائم الأسعار، وصفات الخدمات، حدود المبيعات، نوع المخزن)
-- ---------------------------------------------------------------------------

-- 1) إعدادات تشغيلية مُصنّفة (لا Key/Value — القرار N1)
ALTER TABLE app_settings ADD COLUMN booking_default_due_days smallint NOT NULL DEFAULT 30
    CHECK (booking_default_due_days BETWEEN 0 AND 365);
ALTER TABLE app_settings ADD COLUMN block_expired_issue boolean NOT NULL DEFAULT true;
ALTER TABLE app_settings ADD COLUMN delivery_requires_collection smallint NOT NULL DEFAULT 0
    CHECK (delivery_requires_collection BETWEEN 0 AND 2);
ALTER TABLE app_settings ADD COLUMN reserve_stock_on_booking boolean NOT NULL DEFAULT true;
ALTER TABLE app_settings ADD COLUMN auto_invoice_on_delivery boolean NOT NULL DEFAULT true;
ALTER TABLE app_settings ADD COLUMN invoice_grouping_mode smallint NOT NULL DEFAULT 0
    CHECK (invoice_grouping_mode BETWEEN 0 AND 2);
ALTER TABLE app_settings ADD COLUMN appointment_slot_minutes smallint NOT NULL DEFAULT 15
    CHECK (appointment_slot_minutes BETWEEN 5 AND 240);
ALTER TABLE app_settings ADD COLUMN pos_receipt_footer varchar(300);
ALTER TABLE app_settings ADD COLUMN auto_follow_up_overdue boolean NOT NULL DEFAULT false;

COMMENT ON COLUMN app_settings.delivery_requires_collection IS '0 = لا / 1 = اختياري / 2 = إجباري';
COMMENT ON COLUMN app_settings.invoice_grouping_mode IS '0 = فاتورة فورية / 1 = عند إغلاق الحجز / 2 = يدوي';

-- 2) توسيع posting_defaults + الجدول المرجعي (N2)
ALTER TABLE posting_defaults ADD COLUMN party_type char(1) NULL;
ALTER TABLE posting_defaults ADD CONSTRAINT ck_posting_defaults_party
    CHECK (party_type IS NULL OR party_type IN ('C','S'));

CREATE TABLE posting_default_types (
    key         varchar(50) PRIMARY KEY,
    module      varchar(30) NOT NULL,
    party_type  char(1) NULL CHECK (party_type IN ('C','S')),
    is_required boolean NOT NULL DEFAULT true,
    sort_order  smallint NOT NULL DEFAULT 0,
    label_ar    varchar(150) NOT NULL,
    label_en    varchar(150),
    is_active   boolean NOT NULL DEFAULT true
);

INSERT INTO posting_default_types (key, module, party_type, is_required, sort_order, label_ar, label_en)
VALUES
    ('stock_inventory', 'inventory', NULL, true, 10, 'حساب المخزون لتصنيف الصنف', 'Inventory account'),
    ('stock_cogs', 'inventory', NULL, true, 20, 'تكلفة صرف المخزون', 'Stock issue / COGS'),
    ('stock_adjustment_gain', 'inventory', NULL, false, 30, 'فروق الجرد - ربح', 'Stock gain'),
    ('stock_adjustment_loss', 'inventory', NULL, false, 40, 'فروق الجرد - خسارة', 'Stock loss'),
    ('opening_equity', 'inventory', NULL, false, 50, 'حساب موازنة الافتتاحي', 'Opening equity'),
    ('rounding', 'inventory', NULL, false, 60, 'فروق تقريب المخزون', 'Stock rounding'),
    ('sales_revenue', 'sales', NULL, true, 110, 'إيرادات المبيعات', 'Sales revenue'),
    ('vat_output', 'sales', NULL, false, 120, 'ضريبة المخرجات', 'VAT output'),
    ('customer_receivable', 'sales', 'C', true, 130, 'مدينو العملاء', 'Customer receivable'),
    ('supplier_payable', 'purchasing', 'S', true, 210, 'دائنو الموردين', 'Supplier payable'),
    ('vat_input', 'purchasing', NULL, false, 220, 'ضريبة المدخلات', 'VAT input'),
    ('purchase_variance', 'purchasing', NULL, false, 230, 'فروق أسعار المشتريات', 'Purchase variance'),
    ('rounding_difference', 'sales', NULL, false, 300, 'فروق التقريب', 'Rounding difference')
ON CONFLICT (key) DO NOTHING;

-- 3) الموظفون (D8/D9)
CREATE TABLE employees (
    id                   uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    is_active            boolean     NOT NULL DEFAULT true,
    version              integer     NOT NULL DEFAULT 1,
    created_at           timestamptz NOT NULL DEFAULT now(),
    created_by           uuid REFERENCES users(id) ON DELETE SET NULL,
    updated_at           timestamptz,
    updated_by           uuid REFERENCES users(id) ON DELETE SET NULL,
    code                 varchar(30)  NOT NULL,
    name_ar              varchar(200) NOT NULL,
    name_en              varchar(200),
    phone                varchar(30),
    email                varchar(200),
    user_id              uuid REFERENCES users(id) ON DELETE SET NULL,
    is_booking_receiver  boolean NOT NULL DEFAULT false,
    is_seller            boolean NOT NULL DEFAULT false,
    is_service_executor  boolean NOT NULL DEFAULT false,
    hire_date            date,
    notes                text,
    search_text          text GENERATED ALWAYS AS (
        normalize_search(coalesce(code,'') || ' ' || coalesce(name_ar,'') || ' ' ||
                         coalesce(name_en,'') || ' ' || coalesce(phone,''))
    ) STORED,
    CONSTRAINT ux_employees_code UNIQUE (code)
);

CREATE INDEX ix_employees_search ON employees USING gin (search_text gin_trgm_ops);
CREATE INDEX ix_employees_active ON employees (is_active, code);
CREATE INDEX ix_employees_user ON employees (user_id);

CREATE TRIGGER trg_touch_employees BEFORE UPDATE ON employees
FOR EACH ROW EXECUTE FUNCTION fn_touch_version();

-- 4) الضرائب (D6)
CREATE TABLE taxes (
    id                  uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    is_active           boolean     NOT NULL DEFAULT true,
    version             integer     NOT NULL DEFAULT 1,
    created_at          timestamptz NOT NULL DEFAULT now(),
    created_by          uuid REFERENCES users(id) ON DELETE SET NULL,
    updated_at          timestamptz,
    updated_by          uuid REFERENCES users(id) ON DELETE SET NULL,
    code                varchar(20)  NOT NULL,
    name_ar             varchar(100) NOT NULL,
    name_en             varchar(100),
    rate                numeric(5,2) NOT NULL CHECK (rate BETWEEN 0 AND 100),
    is_inclusive_allowed boolean    NOT NULL DEFAULT true,
    output_account_id   uuid REFERENCES accounts(id) ON DELETE RESTRICT,
    input_account_id    uuid REFERENCES accounts(id) ON DELETE RESTRICT,
    notes               text,
    CONSTRAINT ux_taxes_code UNIQUE (code),
    CONSTRAINT ck_taxes_accounts CHECK (output_account_id IS NULL OR input_account_id IS NULL
                                        OR output_account_id <> input_account_id)
);

CREATE TRIGGER trg_touch_taxes BEFORE UPDATE ON taxes
FOR EACH ROW EXECUTE FUNCTION fn_touch_version();

-- 5) شروط الدفع
CREATE TABLE payment_terms (
    id               uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    is_active        boolean     NOT NULL DEFAULT true,
    version          integer     NOT NULL DEFAULT 1,
    created_at       timestamptz NOT NULL DEFAULT now(),
    created_by       uuid REFERENCES users(id) ON DELETE SET NULL,
    updated_at       timestamptz,
    updated_by       uuid REFERENCES users(id) ON DELETE SET NULL,
    code             varchar(20)  NOT NULL,
    name_ar          varchar(150) NOT NULL,
    name_en          varchar(150),
    net_days         smallint    NOT NULL DEFAULT 0 CHECK (net_days BETWEEN 0 AND 365),
    discount_percent numeric(5,2) NOT NULL DEFAULT 0 CHECK (discount_percent BETWEEN 0 AND 100),
    discount_days    smallint    NOT NULL DEFAULT 0 CHECK (discount_days BETWEEN 0 AND 365),
    notes            text,
    CONSTRAINT ux_payment_terms_code UNIQUE (code)
);

CREATE TRIGGER trg_touch_payment_terms BEFORE UPDATE ON payment_terms
FOR EACH ROW EXECUTE FUNCTION fn_touch_version();

-- 6) قوائم الأسعار
CREATE TABLE price_lists (
    id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    is_active  boolean     NOT NULL DEFAULT true,
    version    integer     NOT NULL DEFAULT 1,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid REFERENCES users(id) ON DELETE SET NULL,
    updated_at timestamptz,
    updated_by uuid REFERENCES users(id) ON DELETE SET NULL,
    code       varchar(20)  NOT NULL,
    name_ar    varchar(150) NOT NULL,
    name_en    varchar(150),
    currency   char(3)      NOT NULL DEFAULT 'EGP',
    is_default boolean       NOT NULL DEFAULT false,
    valid_from date,
    valid_to   date,
    notes      text,
    CONSTRAINT ux_price_lists_code UNIQUE (code),
    CONSTRAINT ck_price_lists_valid CHECK (valid_to IS NULL OR valid_from IS NULL OR valid_to >= valid_from)
);

CREATE UNIQUE INDEX ux_price_lists_default ON price_lists (is_default) WHERE is_default;

CREATE TRIGGER trg_touch_price_lists BEFORE UPDATE ON price_lists
FOR EACH ROW EXECUTE FUNCTION fn_touch_version();

CREATE TABLE price_list_items (
    id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    created_at    timestamptz NOT NULL DEFAULT now(),
    price_list_id uuid NOT NULL REFERENCES price_lists(id) ON DELETE CASCADE,
    item_id       uuid NOT NULL REFERENCES items(id) ON DELETE RESTRICT,
    unit_id       uuid REFERENCES units(id) ON DELETE RESTRICT,
    price         numeric(18,4) NOT NULL CHECK (price >= 0),
    min_qty       numeric(18,4) NOT NULL DEFAULT 0 CHECK (min_qty >= 0),
    CONSTRAINT ux_price_list_items UNIQUE (price_list_id, item_id, unit_id, min_qty)
);

CREATE INDEX ix_price_list_items_item ON price_list_items (item_id, price_list_id);


-- 7) وصفات الخدمات (D14/D15)
CREATE TABLE service_recipes (
    id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    is_active  boolean     NOT NULL DEFAULT true,
    version    integer     NOT NULL DEFAULT 1,
    created_at timestamptz NOT NULL DEFAULT now(),
    created_by uuid REFERENCES users(id) ON DELETE SET NULL,
    updated_at timestamptz,
    updated_by uuid REFERENCES users(id) ON DELETE SET NULL,
    item_id    uuid NOT NULL REFERENCES items(id) ON DELETE CASCADE,
    notes      varchar(500),
    CONSTRAINT ux_service_recipes_item UNIQUE (item_id)
);

CREATE TRIGGER trg_touch_service_recipes BEFORE UPDATE ON service_recipes
FOR EACH ROW EXECUTE FUNCTION fn_touch_version();

CREATE TABLE service_recipe_items (
    id                uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    created_at        timestamptz NOT NULL DEFAULT now(),
    recipe_id         uuid NOT NULL REFERENCES service_recipes(id) ON DELETE CASCADE,
    line_no           integer NOT NULL CHECK (line_no > 0),
    component_item_id uuid NOT NULL REFERENCES items(id) ON DELETE RESTRICT,
    unit_id           uuid NOT NULL REFERENCES units(id) ON DELETE RESTRICT,
    quantity          numeric(18,4) NOT NULL CHECK (quantity > 0),
    consumption_mode  smallint NOT NULL DEFAULT 0 CHECK (consumption_mode BETWEEN 0 AND 2),
    is_required       boolean NOT NULL DEFAULT true,
    notes             varchar(300),
    CONSTRAINT ux_service_recipe_items UNIQUE (recipe_id, line_no)
);

COMMENT ON COLUMN service_recipe_items.consumption_mode IS
    '0 = كمية ثابتة / 1 = قابلة للتعديل / 2 = إدخال يدوي';

CREATE TABLE recipe_substitutes (
    id                 uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    recipe_item_id     uuid NOT NULL REFERENCES service_recipe_items(id) ON DELETE CASCADE,
    substitute_item_id uuid NOT NULL REFERENCES items(id) ON DELETE RESTRICT,
    priority           smallint NOT NULL DEFAULT 0,
    CONSTRAINT ux_recipe_substitutes UNIQUE (recipe_item_id, substitute_item_id)
);

-- 8) حدود المبيعات لكل عميل/فرع
CREATE TABLE customer_branch_limits (
    id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    created_at    timestamptz NOT NULL DEFAULT now(),
    customer_id   uuid NOT NULL REFERENCES customers(id) ON DELETE CASCADE,
    branch_id     uuid NOT NULL REFERENCES branches(id) ON DELETE CASCADE,
    credit_limit  numeric(18,2) NOT NULL DEFAULT 0 CHECK (credit_limit >= 0),
    sales_limit   numeric(18,2) NOT NULL DEFAULT 0 CHECK (sales_limit >= 0),
    is_active     boolean NOT NULL DEFAULT true,
    version       integer NOT NULL DEFAULT 1,
    updated_at    timestamptz,
    updated_by    uuid REFERENCES users(id) ON DELETE SET NULL,
    CONSTRAINT ux_customer_branch_limits UNIQUE (customer_id, branch_id)
);

-- 9) نوع المخزن + إعدادات الفرع (N10)
ALTER TABLE warehouses ADD COLUMN warehouse_type varchar(20) NOT NULL DEFAULT 'MAIN';
ALTER TABLE warehouses ADD CONSTRAINT ck_warehouses_type
    CHECK (warehouse_type IN ('MAIN','CONSUMABLES'));

CREATE TABLE branch_settings (
    branch_id                 uuid PRIMARY KEY REFERENCES branches(id) ON DELETE CASCADE,
    consumables_warehouse_id  uuid REFERENCES warehouses(id) ON DELETE RESTRICT,
    updated_at                timestamptz,
    updated_by                uuid REFERENCES users(id) ON DELETE SET NULL
);

COMMENT ON TABLE branch_settings IS
    'إعدادات لكل فرع: مخزن المستهلكات المستخدم لصرف وصفات الخدمات (D14)';

CREATE OR REPLACE FUNCTION fn_branch_settings_guard()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_type varchar(20);
BEGIN
    IF NEW.consumables_warehouse_id IS NULL THEN
        RETURN NEW;
    END IF;

    SELECT warehouse_type INTO v_type FROM warehouses WHERE id = NEW.consumables_warehouse_id;
    IF v_type IS DISTINCT FROM 'CONSUMABLES' THEN
        RAISE EXCEPTION 'CONSUMABLES_WAREHOUSE_REQUIRED' USING ERRCODE = '23514';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM warehouse_branches
                   WHERE warehouse_id = NEW.consumables_warehouse_id
                     AND branch_id = NEW.branch_id) THEN
        RAISE EXCEPTION 'WAREHOUSE_NOT_IN_BRANCH' USING ERRCODE = '23514';
    END IF;

    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_branch_settings_guard BEFORE INSERT OR UPDATE ON branch_settings
FOR EACH ROW EXECUTE FUNCTION fn_branch_settings_guard();

-- 10) تعديلات الأصناف (D6/D16)
ALTER TABLE items ADD COLUMN tax_id uuid REFERENCES taxes(id) ON DELETE RESTRICT;
ALTER TABLE items ADD COLUMN price_includes_tax boolean NOT NULL DEFAULT false;
ALTER TABLE items ADD COLUMN track_expiry boolean NOT NULL DEFAULT false;
ALTER TABLE items ADD COLUMN is_consumable boolean NOT NULL DEFAULT false;
ALTER TABLE items ADD COLUMN default_price numeric(18,4)
    CHECK (default_price IS NULL OR default_price >= 0);

CREATE INDEX ix_items_tax ON items (tax_id) WHERE tax_id IS NOT NULL;
CREATE INDEX ix_items_expiry ON items (track_expiry, is_active) WHERE track_expiry;

