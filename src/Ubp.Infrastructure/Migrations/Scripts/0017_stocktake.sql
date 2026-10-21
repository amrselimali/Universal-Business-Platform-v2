-- 0017 — الجرد والتسويات: جلسات الجرد وسطورها وربطها بمستند التسوية
-- -------------------------------------------------------------------

CREATE TABLE stocktakes (
    id           uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    code         varchar(40) NOT NULL,
    branch_id    uuid NOT NULL REFERENCES branches(id) ON DELETE RESTRICT,
    warehouse_id uuid NOT NULL REFERENCES warehouses(id) ON DELETE RESTRICT,
    status       varchar(20) NOT NULL DEFAULT 'DRAFT'
                 CHECK (status IN ('DRAFT','POSTED','CANCELLED')),
    notes        text,
    posted_at    timestamptz,
    posted_by    uuid REFERENCES users(id) ON DELETE SET NULL,
    created_by   uuid REFERENCES users(id) ON DELETE SET NULL,
    created_at   timestamptz NOT NULL DEFAULT now(),
    updated_at   timestamptz NOT NULL DEFAULT now(),
    version      integer NOT NULL DEFAULT 1,
    CONSTRAINT ux_stocktakes_code UNIQUE (branch_id, code)
);

CREATE TABLE stocktake_lines (
    id           uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    stocktake_id uuid NOT NULL REFERENCES stocktakes(id) ON DELETE CASCADE,
    item_id      uuid NOT NULL REFERENCES items(id) ON DELETE RESTRICT,
    book_qty     numeric(18,4) NOT NULL DEFAULT 0,
    counted_qty  numeric(18,4) NOT NULL DEFAULT 0 CHECK (counted_qty >= 0),
    difference   numeric(18,4) GENERATED ALWAYS AS (counted_qty - book_qty) STORED,
    unit_cost    numeric(18,6) NOT NULL DEFAULT 0 CHECK (unit_cost >= 0),
    CONSTRAINT ux_stocktake_lines UNIQUE (stocktake_id, item_id)
);

ALTER TABLE stock_documents
    ADD CONSTRAINT fk_stock_documents_stocktake
    FOREIGN KEY (stocktake_id) REFERENCES stocktakes(id) ON DELETE SET NULL;

CREATE INDEX ix_stocktakes_branch ON stocktakes (branch_id, status, created_at DESC);
CREATE INDEX ix_stocktake_lines_stocktake ON stocktake_lines (stocktake_id);

CREATE TRIGGER trg_touch_stocktakes BEFORE UPDATE ON stocktakes
FOR EACH ROW EXECUTE FUNCTION fn_touch_version();

CREATE OR REPLACE FUNCTION fn_stocktake_guard()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        IF OLD.status <> 'DRAFT' THEN
            RAISE EXCEPTION 'POSTED_STOCKTAKE_IMMUTABLE' USING ERRCODE = '23514';
        END IF;
        RETURN OLD;
    END IF;

    IF OLD.status <> 'DRAFT' AND (
        NEW.code IS DISTINCT FROM OLD.code OR
        NEW.branch_id <> OLD.branch_id OR
        NEW.warehouse_id <> OLD.warehouse_id) THEN
        RAISE EXCEPTION 'POSTED_STOCKTAKE_IMMUTABLE' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_stocktake_guard BEFORE UPDATE OR DELETE ON stocktakes
FOR EACH ROW EXECUTE FUNCTION fn_stocktake_guard();

CREATE OR REPLACE FUNCTION fn_stocktake_line_guard()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_status varchar;
BEGIN
    SELECT status INTO v_status FROM stocktakes
    WHERE id = CASE WHEN TG_OP = 'DELETE' THEN OLD.stocktake_id ELSE NEW.stocktake_id END;

    IF v_status IS DISTINCT FROM 'DRAFT' THEN
        RAISE EXCEPTION 'POSTED_STOCKTAKE_IMMUTABLE' USING ERRCODE = '23514';
    END IF;
    RETURN CASE WHEN TG_OP = 'DELETE' THEN OLD ELSE NEW END;
END;
$$;

CREATE TRIGGER trg_stocktake_line_guard BEFORE INSERT OR UPDATE OR DELETE ON stocktake_lines
FOR EACH ROW EXECUTE FUNCTION fn_stocktake_line_guard();
