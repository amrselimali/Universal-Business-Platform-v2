-- 0016 — نواة المخزون: المستندات، السطور، الحركات، الأرصدة، التحويلات، ومحرك الترحيل الذري
-- ---------------------------------------------------------------------------------

CREATE TABLE stock_documents (
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    doc_no          varchar(40),
    doc_type        varchar(20) NOT NULL
                    CHECK (doc_type IN ('RECEIPT','ISSUE','TRANSFER','ADJUSTMENT','OPENING','REVERSAL')),
    doc_date        date NOT NULL,
    branch_id       uuid NOT NULL REFERENCES branches(id) ON DELETE RESTRICT,
    warehouse_id    uuid NOT NULL REFERENCES warehouses(id) ON DELETE RESTRICT,
    to_branch_id    uuid REFERENCES branches(id) ON DELETE RESTRICT,
    to_warehouse_id uuid REFERENCES warehouses(id) ON DELETE RESTRICT,
    status          varchar(20) NOT NULL DEFAULT 'DRAFT'
                    CHECK (status IN ('DRAFT','POSTED','PARTIALLY_RECEIVED','RECEIVED','CANCELLED')),
    notes           text,
    total_cost      numeric(18,4) NOT NULL DEFAULT 0,
    reversal_of_id  uuid REFERENCES stock_documents(id) ON DELETE RESTRICT,
    stocktake_id    uuid,
    posted_at       timestamptz,
    posted_by       uuid REFERENCES users(id) ON DELETE SET NULL,
    created_by      uuid REFERENCES users(id) ON DELETE SET NULL,
    created_at      timestamptz NOT NULL DEFAULT now(),
    updated_at      timestamptz NOT NULL DEFAULT now(),
    version         integer NOT NULL DEFAULT 1,
    CONSTRAINT ux_stock_documents_no UNIQUE (branch_id, doc_no),
    CONSTRAINT ck_stock_documents_number CHECK (
        status IN ('DRAFT','CANCELLED') OR doc_no IS NOT NULL),
    CONSTRAINT ck_stock_documents_transfer CHECK (
        doc_type <> 'TRANSFER' OR (to_warehouse_id IS NOT NULL AND to_branch_id IS NOT NULL)),
    CONSTRAINT ck_stock_documents_to_wh CHECK (
        to_warehouse_id IS NULL OR to_warehouse_id <> warehouse_id)
);

CREATE UNIQUE INDEX ux_stock_documents_reversal ON stock_documents (reversal_of_id)
WHERE reversal_of_id IS NOT NULL;

CREATE TABLE stock_document_lines (
    id                uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    document_id       uuid NOT NULL REFERENCES stock_documents(id) ON DELETE CASCADE,
    line_no           smallint NOT NULL CHECK (line_no > 0),
    direction         varchar(3) NOT NULL DEFAULT 'IN' CHECK (direction IN ('IN','OUT')),
    item_id           uuid NOT NULL REFERENCES items(id) ON DELETE RESTRICT,
    unit_id           uuid NOT NULL REFERENCES units(id) ON DELETE RESTRICT,
    conversion_factor numeric(18,6) NOT NULL DEFAULT 1 CHECK (conversion_factor > 0),
    quantity          numeric(18,4) NOT NULL CHECK (quantity > 0),
    base_quantity     numeric(18,4) NOT NULL CHECK (base_quantity > 0),
    unit_cost         numeric(18,4) NOT NULL DEFAULT 0 CHECK (unit_cost >= 0),
    total_cost        numeric(18,4) NOT NULL DEFAULT 0 CHECK (total_cost >= 0),
    notes             text,
    CONSTRAINT ux_stock_document_lines UNIQUE (document_id, line_no)
);

CREATE TABLE stock_movements (
    id               bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    document_id      uuid NOT NULL REFERENCES stock_documents(id) ON DELETE RESTRICT,
    document_line_id uuid NOT NULL REFERENCES stock_document_lines(id) ON DELETE RESTRICT,
    item_id          uuid NOT NULL REFERENCES items(id) ON DELETE RESTRICT,
    warehouse_id     uuid NOT NULL REFERENCES warehouses(id) ON DELETE RESTRICT,
    branch_id        uuid NOT NULL REFERENCES branches(id) ON DELETE RESTRICT,
    direction        varchar(3) NOT NULL CHECK (direction IN ('IN','OUT')),
    quantity         numeric(18,4) NOT NULL CHECK (quantity > 0),
    unit_cost        numeric(18,6) NOT NULL,
    total_cost       numeric(18,4) NOT NULL,
    movement_date    date NOT NULL,
    period_id        uuid REFERENCES fiscal_periods(id) ON DELETE SET NULL,
    created_at       timestamptz NOT NULL DEFAULT now(),
    created_by       uuid REFERENCES users(id) ON DELETE SET NULL
);

CREATE TABLE stock_balances (
    item_id         uuid NOT NULL REFERENCES items(id) ON DELETE RESTRICT,
    warehouse_id    uuid NOT NULL REFERENCES warehouses(id) ON DELETE RESTRICT,
    on_hand         numeric(18,4) NOT NULL DEFAULT 0,
    average_cost    numeric(18,6) NOT NULL DEFAULT 0,
    inventory_value numeric(18,4) NOT NULL DEFAULT 0,
    version         bigint NOT NULL DEFAULT 1,
    updated_at      timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (item_id, warehouse_id)
);

CREATE TABLE stock_transfer_receipts (
    id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    document_id uuid NOT NULL REFERENCES stock_documents(id) ON DELETE CASCADE,
    received_at timestamptz NOT NULL DEFAULT now(),
    received_by uuid REFERENCES users(id) ON DELETE SET NULL,
    notes       text
);

CREATE TABLE stock_transfer_receipt_lines (
    id               uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    receipt_id       uuid NOT NULL REFERENCES stock_transfer_receipts(id) ON DELETE CASCADE,
    document_line_id uuid NOT NULL REFERENCES stock_document_lines(id) ON DELETE RESTRICT,
    quantity         numeric(18,4) NOT NULL CHECK (quantity > 0),
    CONSTRAINT ux_transfer_receipt_lines UNIQUE (receipt_id, document_line_id)
);

CREATE INDEX ix_stock_documents_branch ON stock_documents (branch_id, doc_date DESC, id DESC);
CREATE INDEX ix_stock_documents_type ON stock_documents (doc_type, status, id DESC);
CREATE INDEX ix_stock_documents_warehouse ON stock_documents (warehouse_id, doc_date DESC);
CREATE INDEX ix_stock_documents_status ON stock_documents (status, id DESC);
CREATE INDEX ix_stock_lines_document ON stock_document_lines (document_id, line_no);
CREATE INDEX ix_stock_lines_item ON stock_document_lines (item_id);
CREATE INDEX ix_stock_movements_item ON stock_movements (item_id, warehouse_id, id DESC);
CREATE INDEX ix_stock_movements_document ON stock_movements (document_id);
CREATE INDEX ix_stock_movements_date ON stock_movements (movement_date, id DESC);
CREATE INDEX ix_stock_balances_warehouse ON stock_balances (warehouse_id, item_id);
CREATE INDEX ix_transfer_receipts_document ON stock_transfer_receipts (document_id, received_at DESC);

CREATE TRIGGER trg_touch_stock_documents BEFORE UPDATE ON stock_documents
FOR EACH ROW EXECUTE FUNCTION fn_touch_version();

-- الحركات غير قابلة للتعديل أو الحذف
CREATE TRIGGER trg_stock_movements_immutable BEFORE UPDATE OR DELETE ON stock_movements
FOR EACH ROW EXECUTE FUNCTION fn_audit_log_immutable();

-- منع تغيير بيانات مستند مرحّل أو حذفه
CREATE OR REPLACE FUNCTION fn_stock_document_guard()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        IF OLD.status <> 'DRAFT' THEN
            RAISE EXCEPTION 'POSTED_DOCUMENT_IMMUTABLE' USING ERRCODE = '23514';
        END IF;
        RETURN OLD;
    END IF;

    IF OLD.status <> 'DRAFT' AND (
        NEW.doc_no IS DISTINCT FROM OLD.doc_no OR
        NEW.doc_type <> OLD.doc_type OR NEW.doc_date <> OLD.doc_date OR
        NEW.branch_id <> OLD.branch_id OR NEW.warehouse_id <> OLD.warehouse_id OR
        NEW.to_warehouse_id IS DISTINCT FROM OLD.to_warehouse_id OR
        NEW.to_branch_id IS DISTINCT FROM OLD.to_branch_id OR
        NEW.total_cost <> OLD.total_cost) THEN
        RAISE EXCEPTION 'POSTED_DOCUMENT_IMMUTABLE' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_stock_document_guard BEFORE UPDATE OR DELETE ON stock_documents
FOR EACH ROW EXECUTE FUNCTION fn_stock_document_guard();

-- منع تعديل سطور مستند مرحّل
CREATE OR REPLACE FUNCTION fn_stock_line_guard()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_status varchar;
BEGIN
    SELECT status INTO v_status FROM stock_documents
    WHERE id = CASE WHEN TG_OP = 'DELETE' THEN OLD.document_id ELSE NEW.document_id END;

    IF v_status IS DISTINCT FROM 'DRAFT' THEN
        RAISE EXCEPTION 'POSTED_DOCUMENT_IMMUTABLE' USING ERRCODE = '23514';
    END IF;
    RETURN CASE WHEN TG_OP = 'DELETE' THEN OLD ELSE NEW END;
END;
$$;

CREATE TRIGGER trg_stock_line_guard BEFORE INSERT OR UPDATE OR DELETE ON stock_document_lines
FOR EACH ROW EXECUTE FUNCTION fn_stock_line_guard();

-- تطبيق حركة على رصيد (داخل معاملة الترحيل) وإرجاع تكلفة الحركة
-- IN: متوسط مرجح متحرك — OUT: التكلفة المتوسطة السارية
CREATE OR REPLACE FUNCTION fn_stock_apply(
    p_item          uuid,
    p_warehouse     uuid,
    p_direction     varchar,
    p_quantity      numeric,
    p_unit_cost     numeric,
    p_allow_negative boolean,
    p_prefer_given  boolean DEFAULT false
) RETURNS numeric
LANGUAGE plpgsql AS $$
DECLARE
    v_on_hand numeric; v_avg numeric; v_value numeric;
    v_new_qty numeric; v_new_avg numeric; v_new_value numeric; v_move_cost numeric;
BEGIN
    IF p_quantity <= 0 THEN
        RAISE EXCEPTION 'INVALID_QUANTITY' USING ERRCODE = '23514';
    END IF;
    IF p_unit_cost < 0 THEN
        RAISE EXCEPTION 'INVALID_COST' USING ERRCODE = '23514';
    END IF;

    INSERT INTO stock_balances (item_id, warehouse_id)
    VALUES (p_item, p_warehouse)
    ON CONFLICT (item_id, warehouse_id) DO NOTHING;

    SELECT on_hand, average_cost, inventory_value
      INTO v_on_hand, v_avg, v_value
      FROM stock_balances
     WHERE item_id = p_item AND warehouse_id = p_warehouse
       FOR UPDATE;

    IF p_direction = 'IN' THEN
        v_new_qty := v_on_hand + p_quantity;
        v_move_cost := p_quantity * p_unit_cost;
        v_new_value := v_value + v_move_cost;
        v_new_avg := CASE WHEN v_new_qty > 0 THEN v_new_value / v_new_qty ELSE 0 END;
    ELSE
        IF v_on_hand < p_quantity AND NOT p_allow_negative THEN
            RAISE EXCEPTION 'NEGATIVE_STOCK:%', p_item USING ERRCODE = '23514';
        END IF;
        v_move_cost := CASE WHEN p_prefer_given THEN p_quantity * p_unit_cost
                            WHEN v_on_hand > 0 THEN p_quantity * v_avg
                            ELSE p_quantity * p_unit_cost END;
        v_new_qty := v_on_hand - p_quantity;
        IF v_new_qty > 0 THEN
            v_new_value := greatest(v_value - v_move_cost, 0);
            v_new_avg := v_new_value / v_new_qty;
        ELSE
            v_new_value := 0;
            v_new_avg := v_avg;
        END IF;
    END IF;

    UPDATE stock_balances
       SET on_hand = v_new_qty, average_cost = v_new_avg, inventory_value = v_new_value,
           version = version + 1, updated_at = now()
     WHERE item_id = p_item AND warehouse_id = p_warehouse;

    RETURN v_move_cost;
END;
$$;
