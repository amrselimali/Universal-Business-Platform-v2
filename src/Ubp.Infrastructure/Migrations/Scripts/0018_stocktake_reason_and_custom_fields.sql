-- 0018 — سبب فرق الجرد + الحقول المخصصة للأصناف والعملاء والموردين
-- -------------------------------------------------------------

ALTER TABLE stocktake_lines ADD COLUMN reason text;

COMMENT ON COLUMN stocktake_lines.reason IS 'سبب الفرق عند اعتماد الجرد — إلزامي عند وجود فرق';

CREATE OR REPLACE FUNCTION fn_stocktake_reason_guard()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.counted_qty <> NEW.book_qty
       AND (NEW.reason IS NULL OR btrim(NEW.reason) = '') THEN
        RAISE EXCEPTION 'STOCKTAKE_REASON_REQUIRED' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_stocktake_reason_guard BEFORE INSERT OR UPDATE ON stocktake_lines
FOR EACH ROW EXECUTE FUNCTION fn_stocktake_reason_guard();

ALTER TABLE items ADD COLUMN custom_fields jsonb NOT NULL DEFAULT '{}'::jsonb;
ALTER TABLE customers ADD COLUMN custom_fields jsonb NOT NULL DEFAULT '{}'::jsonb;
ALTER TABLE suppliers ADD COLUMN custom_fields jsonb NOT NULL DEFAULT '{}'::jsonb;

COMMENT ON COLUMN items.custom_fields IS 'قيم الحقول المخصصة المعرّفة للكيان items';
COMMENT ON COLUMN customers.custom_fields IS 'قيم الحقول المخصصة المعرّفة للكيان customers';
COMMENT ON COLUMN suppliers.custom_fields IS 'قيم الحقول المخصصة المعرّفة للكيان suppliers';
