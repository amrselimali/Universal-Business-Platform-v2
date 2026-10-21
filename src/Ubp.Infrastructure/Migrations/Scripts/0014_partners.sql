-- 0014 — العملاء والموردون وتصنيفاتهم (نطاق شركة أو فرع)
-- -------------------------------------------------------

CREATE TABLE customer_categories (
    id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    code       varchar(30)  NOT NULL,
    name_ar    varchar(255) NOT NULL,
    name_en    varchar(255),
    is_active  boolean      NOT NULL DEFAULT true,
    search_text text GENERATED ALWAYS AS (
        normalize_search(coalesce(code,'') || ' ' || coalesce(name_ar,'') || ' ' || coalesce(name_en,''))
    ) STORED,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    version    integer NOT NULL DEFAULT 1,
    CONSTRAINT ux_customer_categories_code UNIQUE (code)
);

CREATE TABLE supplier_categories (
    id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    code       varchar(30)  NOT NULL,
    name_ar    varchar(255) NOT NULL,
    name_en    varchar(255),
    is_active  boolean      NOT NULL DEFAULT true,
    search_text text GENERATED ALWAYS AS (
        normalize_search(coalesce(code,'') || ' ' || coalesce(name_ar,'') || ' ' || coalesce(name_en,''))
    ) STORED,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    version    integer NOT NULL DEFAULT 1,
    CONSTRAINT ux_supplier_categories_code UNIQUE (code)
);

CREATE TABLE customers (
    id           uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    code         varchar(50)  NOT NULL,
    name_ar      varchar(255) NOT NULL,
    name_en      varchar(255),
    category_id  uuid REFERENCES customer_categories(id) ON DELETE RESTRICT,
    scope_type   varchar(10)  NOT NULL DEFAULT 'COMPANY',
    branch_id    uuid REFERENCES branches(id) ON DELETE RESTRICT,
    phone        varchar(50),
    email        varchar(255),
    address      varchar(500),
    tax_number   varchar(50),
    credit_limit numeric(18,4) NOT NULL DEFAULT 0 CHECK (credit_limit >= 0),
    notes        text,
    is_active    boolean      NOT NULL DEFAULT true,
    search_text  text GENERATED ALWAYS AS (
        normalize_search(coalesce(code,'') || ' ' || coalesce(name_ar,'') || ' ' ||
                         coalesce(name_en,'') || ' ' || coalesce(phone,'') || ' ' || coalesce(email,''))
    ) STORED,
    created_at   timestamptz NOT NULL DEFAULT now(),
    updated_at   timestamptz NOT NULL DEFAULT now(),
    version      integer NOT NULL DEFAULT 1,
    CONSTRAINT ck_customers_scope CHECK (
        (scope_type = 'COMPANY' AND branch_id IS NULL) OR
        (scope_type = 'BRANCH'  AND branch_id IS NOT NULL)),
    CONSTRAINT ux_customers_scope_code UNIQUE NULLS NOT DISTINCT (scope_type, branch_id, code)
);

CREATE TABLE suppliers (
    id           uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    code         varchar(50)  NOT NULL,
    name_ar      varchar(255) NOT NULL,
    name_en      varchar(255),
    category_id  uuid REFERENCES supplier_categories(id) ON DELETE RESTRICT,
    scope_type   varchar(10)  NOT NULL DEFAULT 'COMPANY',
    branch_id    uuid REFERENCES branches(id) ON DELETE RESTRICT,
    phone        varchar(50),
    email        varchar(255),
    address      varchar(500),
    tax_number   varchar(50),
    credit_limit numeric(18,4) NOT NULL DEFAULT 0 CHECK (credit_limit >= 0),
    notes        text,
    is_active    boolean      NOT NULL DEFAULT true,
    search_text  text GENERATED ALWAYS AS (
        normalize_search(coalesce(code,'') || ' ' || coalesce(name_ar,'') || ' ' ||
                         coalesce(name_en,'') || ' ' || coalesce(phone,'') || ' ' || coalesce(email,''))
    ) STORED,
    created_at   timestamptz NOT NULL DEFAULT now(),
    updated_at   timestamptz NOT NULL DEFAULT now(),
    version      integer NOT NULL DEFAULT 1,
    CONSTRAINT ck_suppliers_scope CHECK (
        (scope_type = 'COMPANY' AND branch_id IS NULL) OR
        (scope_type = 'BRANCH'  AND branch_id IS NOT NULL)),
    CONSTRAINT ux_suppliers_scope_code UNIQUE NULLS NOT DISTINCT (scope_type, branch_id, code)
);

CREATE INDEX ix_customer_categories_search ON customer_categories USING gin (search_text gin_trgm_ops);
CREATE INDEX ix_supplier_categories_search ON supplier_categories USING gin (search_text gin_trgm_ops);
CREATE INDEX ix_customers_search ON customers USING gin (search_text gin_trgm_ops);
CREATE INDEX ix_suppliers_search ON suppliers USING gin (search_text gin_trgm_ops);
CREATE INDEX ix_customers_scope ON customers (scope_type, branch_id, is_active, id);
CREATE INDEX ix_suppliers_scope ON suppliers (scope_type, branch_id, is_active, id);
CREATE INDEX ix_customers_category ON customers (category_id);
CREATE INDEX ix_suppliers_category ON suppliers (category_id);

CREATE TRIGGER trg_touch_customer_categories BEFORE UPDATE ON customer_categories
FOR EACH ROW EXECUTE FUNCTION fn_touch_version();
CREATE TRIGGER trg_touch_supplier_categories BEFORE UPDATE ON supplier_categories
FOR EACH ROW EXECUTE FUNCTION fn_touch_version();
CREATE TRIGGER trg_touch_customers BEFORE UPDATE ON customers
FOR EACH ROW EXECUTE FUNCTION fn_touch_version();
CREATE TRIGGER trg_touch_suppliers BEFORE UPDATE ON suppliers
FOR EACH ROW EXECUTE FUNCTION fn_touch_version();
