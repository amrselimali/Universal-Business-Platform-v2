-- 0031 — المرحلة 4E: فواتير المبيعات والمرتجعات على الجدول الموحد
-- -----------------------------------------------------------------------------

ALTER TABLE commercial_documents ADD COLUMN IF NOT EXISTS booking_id uuid
    REFERENCES bookings(id) ON DELETE RESTRICT;
ALTER TABLE commercial_documents ADD COLUMN IF NOT EXISTS delivered_at timestamptz;

ALTER TABLE commercial_document_lines ADD COLUMN IF NOT EXISTS booking_line_id uuid
    REFERENCES booking_lines(id) ON DELETE RESTRICT;

CREATE INDEX IF NOT EXISTS ix_commercial_documents_booking
    ON commercial_documents (booking_id) WHERE booking_id IS NOT NULL;

-- N24: تتبّع الكميات المرتجعة لكل سطر أصلي
CREATE TABLE IF NOT EXISTS sales_return_links (
    id               uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    return_line_id   uuid NOT NULL UNIQUE
                     REFERENCES commercial_document_lines(id) ON DELETE CASCADE,
    original_line_id uuid NOT NULL
                     REFERENCES commercial_document_lines(id) ON DELETE RESTRICT,
    quantity         numeric(18,4) NOT NULL CHECK (quantity > 0),
    created_at       timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_return_links_original
    ON sales_return_links (original_line_id);

-- D3: لا بند مبيعات بلا بند حجز
CREATE OR REPLACE FUNCTION fn_sales_line_booking_guard()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE v_doc_type varchar;
BEGIN
    SELECT doc_type INTO v_doc_type FROM commercial_documents
    WHERE id = CASE WHEN TG_OP = 'DELETE' THEN OLD.document_id ELSE NEW.document_id END;

    IF v_doc_type IN ('sales_invoice', 'sales_return') AND NEW.booking_line_id IS NULL THEN
        RAISE EXCEPTION 'SALES_LINE_REQUIRES_BOOKING' USING ERRCODE = '23514';
    END IF;
    RETURN CASE WHEN TG_OP = 'DELETE' THEN OLD ELSE NEW END;
END;
$$;

DROP TRIGGER IF EXISTS trg_sales_line_booking_guard ON commercial_document_lines;
CREATE TRIGGER trg_sales_line_booking_guard
    BEFORE INSERT OR UPDATE ON commercial_document_lines
    FOR EACH ROW EXECUTE FUNCTION fn_sales_line_booking_guard();

-- N24: حارس كمية المرتجع + تحديث returned_qty على السطر الأصلي
CREATE OR REPLACE FUNCTION fn_sales_return_guard()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_original_qty numeric;
    v_returned     numeric;
BEGIN
    SELECT quantity INTO v_original_qty
    FROM commercial_document_lines WHERE id = NEW.original_line_id;

    SELECT COALESCE(SUM(quantity), 0) INTO v_returned
    FROM sales_return_links
    WHERE original_line_id = NEW.original_line_id AND id <> NEW.id;

    v_returned := v_returned + NEW.quantity;
    IF v_returned > v_original_qty THEN
        RAISE EXCEPTION 'RETURN_QTY_EXCEEDED' USING ERRCODE = '23514';
    END IF;

    UPDATE commercial_document_lines SET returned_qty = v_returned
    WHERE id = NEW.original_line_id;

    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_sales_return_guard ON sales_return_links;
CREATE TRIGGER trg_sales_return_guard
    BEFORE INSERT OR UPDATE ON sales_return_links
    FOR EACH ROW EXECUTE FUNCTION fn_sales_return_guard();

COMMENT ON FUNCTION fn_sales_line_booking_guard() IS
    'D3: كل بند مبيعات/مرتجع مبيعات يجب أن يحمل booking_line_id';
COMMENT ON FUNCTION fn_sales_return_guard() IS
    'N24: مجموع المرتجع لكل سطر أصلي لا يتجاوز كميته';

-- السطر الأصلي في فاتورة مرحّلة: يُسمح بتحديث returned_qty وحده (تتبّع المرتجعات N24)
CREATE OR REPLACE FUNCTION fn_commercial_line_guard()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_status varchar;
BEGIN
    IF TG_OP = 'DELETE' AND NOT EXISTS
        (SELECT 1 FROM commercial_documents WHERE id = OLD.document_id) THEN
        RETURN OLD;
    END IF;

    SELECT status INTO v_status FROM commercial_documents
    WHERE id = CASE WHEN TG_OP = 'DELETE' THEN OLD.document_id ELSE NEW.document_id END;

    IF v_status IS DISTINCT FROM 'draft' THEN
        IF TG_OP = 'UPDATE' THEN
            IF (to_jsonb(NEW) - 'returned_qty') = (to_jsonb(OLD) - 'returned_qty') THEN
                RETURN NEW;
            END IF;
        END IF;
        RAISE EXCEPTION 'POSTED_DOCUMENT_IMMUTABLE' USING ERRCODE = '23514';
    END IF;
    RETURN CASE WHEN TG_OP = 'DELETE' THEN OLD ELSE NEW END;
END;
$$;
