-- 0020 — نواة المحاسبة: دليل الحسابات، مراكز التكلفة، دفاتر اليومية، إعدادات الترحيل
-- -------------------------------------------------------------------------------
-- المرحلة 3 (أ): التعريفات المحاسبية فقط — لا قيود ولا أرصدة في هذا السكريبت.

ALTER TABLE app_settings ADD COLUMN auto_post_stock_journals boolean NOT NULL DEFAULT false;
ALTER TABLE app_settings ADD COLUMN accounting_start_date date;

COMMENT ON COLUMN app_settings.auto_post_stock_journals IS
    'ترحيل قيود المخزون تلقائياً عند ترحيل مستند مخزني';
COMMENT ON COLUMN app_settings.accounting_start_date IS
    'بداية السنة المالية المحاسبية — تُستخدم في فحص اكتمال الإعداد وقفل ما قبلها';

-- 1) دليل الحسابات ------------------------------------------------------------
CREATE TABLE accounts (
    id                 uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    code               varchar(20)  NOT NULL,
    name_ar            varchar(200) NOT NULL,
    name_en            varchar(200),
    parent_id          uuid REFERENCES accounts(id) ON DELETE RESTRICT,
    path               varchar(300) NOT NULL,
    level              smallint     NOT NULL DEFAULT 1 CHECK (level BETWEEN 1 AND 10),
    is_group           boolean      NOT NULL DEFAULT false,
    account_type       varchar(30)  NOT NULL CHECK (account_type IN (
                           'asset_receivable','asset_cash','asset_current','asset_fixed',
                           'liability_payable','liability_current','equity','income',
                           'expense','expense_cogs')),
    cost_center_policy smallint     NOT NULL DEFAULT 0 CHECK (cost_center_policy BETWEEN 0 AND 2),
    is_system          boolean      NOT NULL DEFAULT false,
    notes              text,
    is_active          boolean      NOT NULL DEFAULT true,
    version            integer      NOT NULL DEFAULT 1,
    created_at         timestamptz  NOT NULL DEFAULT now(),
    created_by         uuid REFERENCES users(id) ON DELETE SET NULL,
    updated_at         timestamptz,
    updated_by         uuid REFERENCES users(id) ON DELETE SET NULL,
    search_text        text GENERATED ALWAYS AS (
        normalize_search(coalesce(code,'') || ' ' || coalesce(name_ar,'') || ' ' ||
                         coalesce(name_en,''))
    ) STORED,
    CONSTRAINT ux_accounts_code UNIQUE (code),
    CONSTRAINT ck_accounts_not_self CHECK (parent_id IS NULL OR parent_id <> id),
    CONSTRAINT ck_accounts_path CHECK (
        length(path) >= 2 AND left(path, 1) = '/' AND right(path, 1) = '/'
        AND position('/' || code || '/' in path) > 0)
);

COMMENT ON TABLE accounts IS 'دليل الحسابات — شجرة بمسار مادي (path) لتسريع القراءة';
COMMENT ON COLUMN accounts.path IS 'المسار المادي بصيغة /1000/1100/1101/ لتسريع تقارير الشجرة';
COMMENT ON COLUMN accounts.cost_center_policy IS '0 = غير مسموح، 1 = اختياري، 2 = إجباري';


-- 2) مراكز التكلفة ------------------------------------------------------------
CREATE TABLE cost_centers (
    id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    code       varchar(20)  NOT NULL,
    name_ar    varchar(200) NOT NULL,
    name_en    varchar(200),
    parent_id  uuid REFERENCES cost_centers(id) ON DELETE RESTRICT,
    path       varchar(300) NOT NULL,
    level      smallint     NOT NULL DEFAULT 1 CHECK (level BETWEEN 1 AND 10),
    is_group   boolean      NOT NULL DEFAULT false,
    branch_id  uuid REFERENCES branches(id) ON DELETE RESTRICT,
    notes      text,
    is_active  boolean      NOT NULL DEFAULT true,
    version    integer      NOT NULL DEFAULT 1,
    created_at timestamptz  NOT NULL DEFAULT now(),
    created_by uuid REFERENCES users(id) ON DELETE SET NULL,
    updated_at timestamptz,
    updated_by uuid REFERENCES users(id) ON DELETE SET NULL,
    search_text text GENERATED ALWAYS AS (
        normalize_search(coalesce(code,'') || ' ' || coalesce(name_ar,'') || ' ' ||
                         coalesce(name_en,''))
    ) STORED,
    CONSTRAINT ux_cost_centers_code UNIQUE (code),
    CONSTRAINT ck_cost_centers_not_self CHECK (parent_id IS NULL OR parent_id <> id),
    CONSTRAINT ck_cost_centers_path CHECK (
        length(path) >= 2 AND left(path, 1) = '/' AND right(path, 1) = '/'
        AND position('/' || code || '/' in path) > 0)
);

COMMENT ON COLUMN cost_centers.branch_id IS 'NULL = مركز مشترك بين كل الفروع';

CREATE INDEX ix_cost_centers_search ON cost_centers USING gin (search_text gin_trgm_ops);
CREATE INDEX ix_cost_centers_path ON cost_centers (path text_pattern_ops);
CREATE INDEX ix_cost_centers_parent ON cost_centers (parent_id, is_active, code);
CREATE INDEX ix_cost_centers_branch ON cost_centers (branch_id, is_active, code);

-- 3) دفاتر اليومية ------------------------------------------------------------
CREATE TABLE journals (
    id                 uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    code               varchar(10)  NOT NULL,
    name_ar            varchar(150) NOT NULL,
    name_en            varchar(150),
    journal_type       varchar(20)  NOT NULL CHECK (journal_type IN (
                           'general','sales','purchase','cash','bank','stock','opening')),
    default_account_id uuid REFERENCES accounts(id) ON DELETE RESTRICT,
    allow_manual       boolean      NOT NULL DEFAULT true,
    is_system          boolean      NOT NULL DEFAULT false,
    sort_order         integer      NOT NULL DEFAULT 0,
    is_active          boolean      NOT NULL DEFAULT true,
    version            integer      NOT NULL DEFAULT 1,
    created_at         timestamptz  NOT NULL DEFAULT now(),
    created_by         uuid REFERENCES users(id) ON DELETE SET NULL,
    updated_at         timestamptz,
    updated_by         uuid REFERENCES users(id) ON DELETE SET NULL,
    CONSTRAINT ux_journals_code UNIQUE (code)
);

INSERT INTO journals (code, name_ar, name_en, journal_type, allow_manual, is_system, sort_order)
VALUES ('GEN', 'اليومية العامة', 'General Journal', 'general', true, true, 10),
       ('SAL', 'يومية المبيعات', 'Sales Journal', 'sales', false, true, 20),
       ('PUR', 'يومية المشتريات', 'Purchase Journal', 'purchase', false, true, 30),
       ('CSH', 'يومية الخزينة', 'Cash Journal', 'cash', true, true, 40),
       ('BNK', 'يومية البنك', 'Bank Journal', 'bank', true, true, 50),
       ('STK', 'يومية المخزون', 'Stock Journal', 'stock', false, true, 60),
       ('OPN', 'اليومية الافتتاحية', 'Opening Journal', 'opening', true, true, 70)
ON CONFLICT (code) DO NOTHING;

-- 4) إعدادات الترحيل الافتراضية ----------------------------------------------
CREATE TABLE posting_defaults (
    id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    setting_key varchar(50) NOT NULL,
    account_id  uuid REFERENCES accounts(id) ON DELETE RESTRICT,
    is_active   boolean     NOT NULL DEFAULT true,
    version     integer     NOT NULL DEFAULT 1,
    created_at  timestamptz NOT NULL DEFAULT now(),
    created_by  uuid REFERENCES users(id) ON DELETE SET NULL,
    updated_at  timestamptz,
    updated_by  uuid REFERENCES users(id) ON DELETE SET NULL,
    CONSTRAINT ux_posting_defaults_key UNIQUE (setting_key)
);

COMMENT ON TABLE posting_defaults IS
    'حسابات الترحيل الافتراضية: stock_inventory, stock_cogs, stock_adjustment_gain, stock_adjustment_loss, opening_equity, rounding';

-- 5) حسابات تصنيفات الأصناف ---------------------------------------------------
CREATE TABLE item_category_accounts (
    id                         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    category_id                uuid NOT NULL REFERENCES item_categories(id) ON DELETE CASCADE,
    inventory_account_id       uuid NOT NULL REFERENCES accounts(id) ON DELETE RESTRICT,
    cogs_account_id            uuid NOT NULL REFERENCES accounts(id) ON DELETE RESTRICT,
    adjustment_gain_account_id uuid REFERENCES accounts(id) ON DELETE RESTRICT,
    adjustment_loss_account_id uuid REFERENCES accounts(id) ON DELETE RESTRICT,
    is_active                  boolean     NOT NULL DEFAULT true,
    version                    integer     NOT NULL DEFAULT 1,
    created_at                 timestamptz NOT NULL DEFAULT now(),
    created_by                 uuid REFERENCES users(id) ON DELETE SET NULL,
    updated_at                 timestamptz,
    updated_by                 uuid REFERENCES users(id) ON DELETE SET NULL,
    CONSTRAINT ux_item_category_accounts_category UNIQUE (category_id)
);

-- 6) المشغلات: التزامن + حماية الشجرة ----------------------------------------
CREATE TRIGGER trg_touch_accounts BEFORE UPDATE ON accounts
FOR EACH ROW EXECUTE FUNCTION fn_touch_version();
CREATE TRIGGER trg_touch_cost_centers BEFORE UPDATE ON cost_centers
FOR EACH ROW EXECUTE FUNCTION fn_touch_version();
CREATE TRIGGER trg_touch_journals BEFORE UPDATE ON journals
FOR EACH ROW EXECUTE FUNCTION fn_touch_version();
CREATE TRIGGER trg_touch_posting_defaults BEFORE UPDATE ON posting_defaults
FOR EACH ROW EXECUTE FUNCTION fn_touch_version();
CREATE TRIGGER trg_touch_item_category_accounts BEFORE UPDATE ON item_category_accounts
FOR EACH ROW EXECUTE FUNCTION fn_touch_version();

-- منع الدوران في شجرة الحسابات ومنع نقل حساب إلى نفسه أو إلى أحد أبنائه
CREATE OR REPLACE FUNCTION fn_account_tree_guard()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_parent uuid := NEW.parent_id;
    v_guard  integer := 0;
BEGIN
    IF v_parent IS NULL THEN
        RETURN NEW;
    END IF;

    WHILE v_parent IS NOT NULL LOOP
        IF v_parent = NEW.id THEN
            RAISE EXCEPTION 'ACCOUNT_TREE_CYCLE' USING ERRCODE = '23514';
        END IF;
        v_guard := v_guard + 1;
        IF v_guard > 32 THEN
            RAISE EXCEPTION 'ACCOUNT_TREE_TOO_DEEP' USING ERRCODE = '23514';
        END IF;
        SELECT parent_id INTO v_parent FROM accounts WHERE id = v_parent;
    END LOOP;

    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_account_tree_guard BEFORE INSERT OR UPDATE ON accounts
FOR EACH ROW EXECUTE FUNCTION fn_account_tree_guard();

CREATE OR REPLACE FUNCTION fn_cost_center_tree_guard()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_parent uuid := NEW.parent_id;
    v_guard  integer := 0;
BEGIN
    IF v_parent IS NULL THEN
        RETURN NEW;
    END IF;

    WHILE v_parent IS NOT NULL LOOP
        IF v_parent = NEW.id THEN
            RAISE EXCEPTION 'COST_CENTER_TREE_CYCLE' USING ERRCODE = '23514';
        END IF;
        v_guard := v_guard + 1;
        IF v_guard > 32 THEN
            RAISE EXCEPTION 'COST_CENTER_TREE_TOO_DEEP' USING ERRCODE = '23514';
        END IF;
        SELECT parent_id INTO v_parent FROM cost_centers WHERE id = v_parent;
    END LOOP;

    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_cost_center_tree_guard BEFORE INSERT OR UPDATE ON cost_centers
FOR EACH ROW EXECUTE FUNCTION fn_cost_center_tree_guard();

-- منع حذف أو تعطيل الحسابات النظامية
CREATE OR REPLACE FUNCTION fn_account_system_guard()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP = 'DELETE' AND OLD.is_system THEN
        RAISE EXCEPTION 'SYSTEM_ACCOUNT_IMMUTABLE' USING ERRCODE = '23514';
    END IF;
    IF TG_OP = 'UPDATE' AND OLD.is_system AND OLD.is_active AND NOT NEW.is_active THEN
        RAISE EXCEPTION 'SYSTEM_ACCOUNT_IMMUTABLE' USING ERRCODE = '23514';
    END IF;
    RETURN CASE WHEN TG_OP = 'DELETE' THEN OLD ELSE NEW END;
END;
$$;

CREATE TRIGGER trg_account_system_guard BEFORE UPDATE OR DELETE ON accounts
FOR EACH ROW EXECUTE FUNCTION fn_account_system_guard();

-- منع حذف حساب مستخدم في قيود أو إعدادات أو له أبناء
CREATE OR REPLACE FUNCTION fn_account_delete_guard()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF EXISTS (SELECT 1 FROM journal_entry_lines WHERE account_id = OLD.id)
       OR EXISTS (SELECT 1 FROM posting_defaults WHERE account_id = OLD.id)
       OR EXISTS (SELECT 1 FROM accounts WHERE parent_id = OLD.id) THEN
        RAISE EXCEPTION 'ACCOUNT_IN_USE' USING ERRCODE = '23503';
    END IF;
    RETURN OLD;
END;
$$;

CREATE TRIGGER trg_account_delete_guard BEFORE DELETE ON accounts
FOR EACH ROW EXECUTE FUNCTION fn_account_delete_guard();


CREATE INDEX ix_accounts_search ON accounts USING gin (search_text gin_trgm_ops);
CREATE INDEX ix_accounts_path ON accounts (path text_pattern_ops);
CREATE INDEX ix_accounts_parent ON accounts (parent_id, is_active, code);
CREATE INDEX ix_accounts_active ON accounts (is_active, code);
CREATE INDEX ix_accounts_type ON accounts (account_type, is_active);
