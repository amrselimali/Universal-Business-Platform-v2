-- 0025 — المرحلة 4A-2: دفعات المخزون (Lots) + FEFO + تتبّع الصلاحية + إعدام المنتهي
-- -----------------------------------------------------------------------------

-- 1) بيانات الدفعة على بند المستند (تُسجَّل عند الدخول فقط — D16)
ALTER TABLE stock_document_lines ADD COLUMN lot_number varchar(60);
ALTER TABLE stock_document_lines ADD COLUMN expiry_date date;

-- 2) الدفعات
CREATE TABLE stock_lots (
    id                uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    item_id           uuid NOT NULL REFERENCES items(id) ON DELETE RESTRICT,
    warehouse_id      uuid NOT NULL REFERENCES warehouses(id) ON DELETE RESTRICT,
    lot_number        varchar(60) NOT NULL,
    manufacture_date  date,
    expiry_date       date,
    quantity_on_hand  numeric(18,4) NOT NULL DEFAULT 0 CHECK (quantity_on_hand >= 0),
    quantity_reserved numeric(18,4) NOT NULL DEFAULT 0 CHECK (quantity_reserved >= 0),
    unit_cost         numeric(18,6) NOT NULL DEFAULT 0 CHECK (unit_cost >= 0),
    received_at       timestamptz NOT NULL DEFAULT now(),
    source_document_id uuid REFERENCES stock_documents(id) ON DELETE SET NULL,
    is_active         boolean NOT NULL DEFAULT true,
    version           integer NOT NULL DEFAULT 1,
    created_at        timestamptz NOT NULL DEFAULT now(),
    updated_at        timestamptz,
    CONSTRAINT ux_stock_lots UNIQUE (item_id, warehouse_id, lot_number),
    CONSTRAINT ck_stock_lots_reserved CHECK (quantity_reserved <= quantity_on_hand),
    CONSTRAINT ck_stock_lots_expiry CHECK (expiry_date IS NULL OR manufacture_date IS NULL
                                           OR expiry_date >= manufacture_date)
);

COMMENT ON TABLE stock_lots IS
    'دفعات المخزون: المصدر الوحيد لأرصدة الأصناف المتتبَّعة بالصلاحية (FEFO)';

CREATE INDEX ix_stock_lots_fefo ON stock_lots (item_id, warehouse_id, expiry_date NULLS LAST, id)
    WHERE quantity_on_hand > 0;
CREATE INDEX ix_stock_lots_expiry ON stock_lots (expiry_date)
    WHERE quantity_on_hand > 0 AND expiry_date IS NOT NULL;

CREATE TRIGGER trg_touch_stock_lots BEFORE UPDATE ON stock_lots
FOR EACH ROW EXECUTE FUNCTION fn_touch_version();

CREATE TABLE stock_lot_movements (
    id               bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    lot_id           uuid NOT NULL REFERENCES stock_lots(id) ON DELETE RESTRICT,
    document_id      uuid NOT NULL REFERENCES stock_documents(id) ON DELETE RESTRICT,
    document_line_id uuid REFERENCES stock_document_lines(id) ON DELETE SET NULL,
    direction        char(1) NOT NULL CHECK (direction IN ('I','O')),
    quantity         numeric(18,4) NOT NULL CHECK (quantity > 0),
    unit_cost        numeric(18,6) NOT NULL DEFAULT 0,
    created_at       timestamptz NOT NULL DEFAULT now(),
    created_by       uuid REFERENCES users(id) ON DELETE SET NULL
);

CREATE INDEX ix_stock_lot_movements_lot ON stock_lot_movements (lot_id, created_at DESC);
CREATE INDEX ix_stock_lot_movements_doc ON stock_lot_movements (document_id);

-- 3) كاش الحجز على مستوى الرصيد (N11)
ALTER TABLE stock_balances ADD COLUMN reserved_qty numeric(18,4) NOT NULL DEFAULT 0
    CHECK (reserved_qty >= 0);
ALTER TABLE stock_balances ADD CONSTRAINT ck_stock_balances_reserved
    CHECK (reserved_qty <= GREATEST(on_hand, 0));

-- 4) نوع مستند الإعدام
ALTER TABLE stock_documents DROP CONSTRAINT IF EXISTS stock_documents_doc_type_check;
ALTER TABLE stock_documents ADD CONSTRAINT ck_stock_documents_type
    CHECK (doc_type IN ('RECEIPT','ISSUE','TRANSFER','ADJUSTMENT','OPENING','REVERSAL','DISPOSAL'));

-- 5) Backfill: دفعة اصطناعية لكل (صنف، مخزن) له رصيد قائم
INSERT INTO stock_lots (id, item_id, warehouse_id, lot_number, expiry_date,
                        quantity_on_hand, unit_cost, received_at)
SELECT gen_random_uuid(), b.item_id, b.warehouse_id, 'OPENING', NULL,
       b.on_hand, b.average_cost, now()
FROM stock_balances b
WHERE b.on_hand > 0
ON CONFLICT (item_id, warehouse_id, lot_number) DO NOTHING;

-- 6) محرك الصرف بواسطة الدفعات: IN يُنشئ/يعبّئ دفعة، وOUT يخصم FEFO
CREATE OR REPLACE FUNCTION fn_stock_apply_lot(
    p_item          uuid,
    p_warehouse     uuid,
    p_direction     varchar,
    p_quantity      numeric,
    p_unit_cost     numeric,
    p_allow_negative boolean,
    p_lot_number    varchar DEFAULT NULL,
    p_expiry        date    DEFAULT NULL,
    p_document      uuid    DEFAULT NULL,
    p_document_line uuid    DEFAULT NULL,
    p_prefer_given  boolean DEFAULT false
) RETURNS numeric
LANGUAGE plpgsql
AS $$
DECLARE
    v_track        boolean;
    v_block_expiry boolean;
    v_lot_id       uuid;
    v_lot_number   varchar(60);
    v_remaining    numeric := p_quantity;
    v_take         numeric;
    v_total_cost   numeric := 0;
    v_lot          record;
BEGIN
    SELECT track_expiry INTO v_track FROM items WHERE id = p_item;
    IF v_track IS NOT TRUE THEN
        RETURN fn_stock_apply(p_item, p_warehouse, p_direction, p_quantity, p_unit_cost,
                              p_allow_negative, p_prefer_given);
    END IF;

    IF p_direction = 'IN' THEN
        v_lot_number := COALESCE(NULLIF(btrim(p_lot_number), ''),
                                 'LOT-' || to_char(COALESCE(p_expiry, current_date), 'YYYYMMDD'));

        INSERT INTO stock_lots AS s
            (id, item_id, warehouse_id, lot_number, expiry_date, quantity_on_hand,
             unit_cost, source_document_id)
        VALUES
            (gen_random_uuid(), p_item, p_warehouse, v_lot_number, p_expiry, 0,
             p_unit_cost, p_document)
        ON CONFLICT (item_id, warehouse_id, lot_number) DO UPDATE
            SET expiry_date = COALESCE(s.expiry_date, EXCLUDED.expiry_date),
                updated_at = now()
        RETURNING s.id INTO v_lot_id;

        UPDATE stock_lots
           SET unit_cost = CASE WHEN quantity_on_hand = 0 THEN p_unit_cost
                                ELSE round((quantity_on_hand * unit_cost + p_quantity * p_unit_cost)
                                           / (quantity_on_hand + p_quantity), 6) END,
               quantity_on_hand = quantity_on_hand + p_quantity,
               source_document_id = COALESCE(source_document_id, p_document),
               updated_at = now()
         WHERE id = v_lot_id;

        INSERT INTO stock_lot_movements
            (lot_id, document_id, document_line_id, direction, quantity, unit_cost)
        VALUES
            (v_lot_id, p_document, p_document_line, 'I', p_quantity, p_unit_cost)
        ON CONFLICT DO NOTHING;

        RETURN fn_stock_apply(p_item, p_warehouse, 'IN', p_quantity, p_unit_cost,
                              p_allow_negative, p_prefer_given);
    END IF;

    -- OUT: تخصيص FEFO (الأقرب انتهاءً أولاً، وغير المنتهي بعدها)
    SELECT COALESCE(block_expired_issue, true) INTO v_block_expiry FROM app_settings LIMIT 1;

    FOR v_lot IN
        SELECT id, lot_number, quantity_on_hand, unit_cost, expiry_date
        FROM stock_lots
        WHERE item_id = p_item AND warehouse_id = p_warehouse AND quantity_on_hand > 0
        ORDER BY expiry_date NULLS LAST, id
        FOR UPDATE
    LOOP
        EXIT WHEN v_remaining <= 0;

        IF v_block_expiry AND v_lot.expiry_date IS NOT NULL
           AND v_lot.expiry_date < current_date THEN
            RAISE EXCEPTION 'EXPIRED_LOT:%', v_lot.lot_number USING ERRCODE = '23514';
        END IF;

        v_take := LEAST(v_remaining, v_lot.quantity_on_hand);

        UPDATE stock_lots
           SET quantity_on_hand = quantity_on_hand - v_take,
               quantity_reserved = LEAST(quantity_reserved, quantity_on_hand - v_take),
               updated_at = now()
         WHERE id = v_lot.id;

        INSERT INTO stock_lot_movements
            (lot_id, document_id, document_line_id, direction, quantity, unit_cost)
        VALUES
            (v_lot.id, p_document, p_document_line, 'O', v_take, v_lot.unit_cost)
        ON CONFLICT DO NOTHING;

        v_total_cost := v_total_cost + (v_take * v_lot.unit_cost);
        v_remaining := v_remaining - v_take;
    END LOOP;

    IF v_remaining > 0 THEN
        IF NOT p_allow_negative THEN
            RAISE EXCEPTION 'NEGATIVE_STOCK:%', p_item USING ERRCODE = '23514';
        END IF;
        v_total_cost := v_total_cost + (v_remaining * p_unit_cost);
    END IF;

    PERFORM fn_stock_apply(p_item, p_warehouse, 'OUT', p_quantity,
                           v_total_cost / p_quantity, p_allow_negative, true);
    RETURN v_total_cost;
END;
$$;

COMMENT ON FUNCTION fn_stock_apply_lot(uuid, uuid, varchar, numeric, numeric, boolean,
    varchar, date, uuid, uuid, boolean) IS
    'محرك المخزون للدفعات: IN يملأ الدفعة، OUT يخصم FEFO ويمنع صرف المنتهي عند تفعيل الإعداد';

-- دالة مساعدة: المتاح (المتاح = الموجود - المحجوز) لصنف في مخزن
CREATE OR REPLACE FUNCTION fn_stock_available(p_item uuid, p_warehouse uuid)
RETURNS numeric
LANGUAGE sql
STABLE
AS $$
    SELECT COALESCE(on_hand, 0) - COALESCE(reserved_qty, 0)
    FROM stock_balances WHERE item_id = p_item AND warehouse_id = p_warehouse
$$;

