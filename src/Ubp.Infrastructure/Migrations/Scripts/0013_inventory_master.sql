-- 0013 — البيانات الأساسية للمخزون: الوحدات، تصنيفات الأصناف، الأصناف، وحدات الصنف، الباركود
-- ----------------------------------------------------------------------------------

CREATE TABLE units (
    id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    code       varchar(30)  NOT NULL,
    name_ar    varchar(255) NOT NULL,
    name_en    varchar(255),
    decimals   smallint     NOT NULL DEFAULT 2 CHECK (decimals BETWEEN 0 AND 6),
    is_active  boolean      NOT NULL DEFAULT true,
    search_text text GENERATED ALWAYS AS (
        normalize_search(coalesce(code,'') || ' ' || coalesce(name_ar,'') || ' ' || coalesce(name_en,''))
    ) STORED,
    created_at timestamptz  NOT NULL DEFAULT now(),
    updated_at timestamptz  NOT NULL DEFAULT now(),
    version    integer      NOT NULL DEFAULT 1,
    CONSTRAINT ux_units_code UNIQUE (code)
);

CREATE TABLE item_categories (
    id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    code       varchar(30)  NOT NULL,
    name_ar    varchar(255) NOT NULL,
    name_en    varchar(255),
    parent_id  uuid REFERENCES item_categories(id) ON DELETE RESTRICT,
    is_active  boolean      NOT NULL DEFAULT true,
    search_text text GENERATED ALWAYS AS (
        normalize_search(coalesce(code,'') || ' ' || coalesce(name_ar,'') || ' ' || coalesce(name_en,''))
    ) STORED,
    created_at timestamptz  NOT NULL DEFAULT now(),
    updated_at timestamptz  NOT NULL DEFAULT now(),
    version    integer      NOT NULL DEFAULT 1,
    CONSTRAINT ux_item_categories_code UNIQUE (code),
    CONSTRAINT ck_item_categories_not_self CHECK (parent_id IS NULL OR parent_id <> id)
);

CREATE TABLE items (
    id           uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    code         varchar(50)  NOT NULL,
    name_ar      varchar(255) NOT NULL,
    name_en      varchar(255),
    category_id  uuid REFERENCES item_categories(id) ON DELETE RESTRICT,
    base_unit_id uuid NOT NULL REFERENCES units(id) ON DELETE RESTRICT,
    item_type    varchar(20)  NOT NULL DEFAULT 'GOODS' CHECK (item_type IN ('GOODS','SERVICE')),
    notes        text,
    is_active    boolean      NOT NULL DEFAULT true,
    search_text  text GENERATED ALWAYS AS (
        normalize_search(coalesce(code,'') || ' ' || coalesce(name_ar,'') || ' ' || coalesce(name_en,''))
    ) STORED,
    created_at   timestamptz  NOT NULL DEFAULT now(),
    updated_at   timestamptz  NOT NULL DEFAULT now(),
    version      integer      NOT NULL DEFAULT 1,
    CONSTRAINT ux_items_code UNIQUE (code)
);

CREATE TABLE item_units (
    id                uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    item_id           uuid NOT NULL REFERENCES items(id) ON DELETE CASCADE,
    unit_id           uuid NOT NULL REFERENCES units(id) ON DELETE RESTRICT,
    conversion_factor numeric(18,6) NOT NULL DEFAULT 1 CHECK (conversion_factor > 0),
    sale_price        numeric(18,4) CHECK (sale_price IS NULL OR sale_price >= 0),
    purchase_price    numeric(18,4) CHECK (purchase_price IS NULL OR purchase_price >= 0),
    is_active         boolean NOT NULL DEFAULT true,
    created_at        timestamptz NOT NULL DEFAULT now(),
    updated_at        timestamptz NOT NULL DEFAULT now(),
    version           integer NOT NULL DEFAULT 1,
    CONSTRAINT ux_item_units UNIQUE (item_id, unit_id)
);

CREATE TABLE item_barcodes (
    id         uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    item_id    uuid NOT NULL REFERENCES items(id) ON DELETE CASCADE,
    unit_id    uuid REFERENCES units(id) ON DELETE SET NULL,
    barcode    varchar(64) NOT NULL,
    is_active  boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ux_item_barcodes UNIQUE (barcode)
);

CREATE INDEX ix_units_search ON units USING gin (search_text gin_trgm_ops);
CREATE INDEX ix_units_active ON units (is_active, id);
CREATE INDEX ix_item_categories_search ON item_categories USING gin (search_text gin_trgm_ops);
CREATE INDEX ix_item_categories_active ON item_categories (is_active, id);
CREATE INDEX ix_item_categories_parent ON item_categories (parent_id);
CREATE INDEX ix_items_search ON items USING gin (search_text gin_trgm_ops);
CREATE INDEX ix_items_active ON items (is_active, id);
CREATE INDEX ix_items_category ON items (category_id, is_active, id);
CREATE INDEX ix_items_base_unit ON items (base_unit_id);
CREATE INDEX ix_item_units_unit ON item_units (unit_id);
CREATE INDEX ix_item_barcodes_item ON item_barcodes (item_id, is_active);

CREATE TRIGGER trg_touch_units BEFORE UPDATE ON units
FOR EACH ROW EXECUTE FUNCTION fn_touch_version();
CREATE TRIGGER trg_touch_item_categories BEFORE UPDATE ON item_categories
FOR EACH ROW EXECUTE FUNCTION fn_touch_version();
CREATE TRIGGER trg_touch_items BEFORE UPDATE ON items
FOR EACH ROW EXECUTE FUNCTION fn_touch_version();
CREATE TRIGGER trg_touch_item_units BEFORE UPDATE ON item_units
FOR EACH ROW EXECUTE FUNCTION fn_touch_version();
