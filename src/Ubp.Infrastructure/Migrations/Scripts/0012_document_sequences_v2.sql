-- 0012 — ترقيم المستندات v2: إصلاح الطول + دعم الترقيم المركزي + شكل قابل للضبط
-- ---------------------------------------------------------------------------
-- إصلاح 0011: prefix varchar(10) كان أقصر من أكواد المستندات (STOCK_RECEIPT = 13 حرفًا)
-- فلا يمكن أن يعمل الترقيم مع أي نوع مستند افتراضي.

ALTER TABLE document_sequences ALTER COLUMN prefix TYPE varchar(30);
ALTER TABLE document_sequences ALTER COLUMN branch_id DROP NOT NULL;
ALTER TABLE document_sequences ADD COLUMN number_padding smallint NOT NULL DEFAULT 6
    CHECK (number_padding BETWEEN 0 AND 12);
ALTER TABLE document_sequences ADD COLUMN version integer NOT NULL DEFAULT 1;

COMMENT ON COLUMN document_sequences.branch_id IS
    'NULL = عدّاد مركزي على مستوى الشركة، وغير NULL = عدّاد مستقل لكل فرع';
COMMENT ON COLUMN document_sequences.number_padding IS
    'عدد خانات الجزء الرقمي من رقم المستند (6 افتراضيًا)';

ALTER TABLE document_sequences DROP CONSTRAINT ux_document_sequences;
ALTER TABLE document_sequences
    ADD CONSTRAINT ux_document_sequences UNIQUE NULLS NOT DISTINCT (doc_type, branch_id, year);

CREATE TRIGGER trg_touch_document_sequences BEFORE UPDATE ON document_sequences
FOR EACH ROW EXECUTE FUNCTION fn_touch_version();

DROP FUNCTION IF EXISTS next_document_number(varchar, uuid, date, varchar);

CREATE OR REPLACE FUNCTION next_document_number(
    p_doc_type  varchar,
    p_branch_id uuid,
    p_doc_date  date,
    p_prefix    varchar DEFAULT NULL,
    p_padding   smallint DEFAULT 6
) RETURNS varchar
LANGUAGE plpgsql
AS $$
DECLARE
    v_year    smallint := EXTRACT(YEAR FROM p_doc_date)::smallint;
    v_prefix  varchar;
    v_number  integer;
    v_padding smallint;
BEGIN
    IF p_branch_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM branches WHERE id = p_branch_id) THEN
        RAISE EXCEPTION 'BRANCH_NOT_FOUND' USING ERRCODE = '23503';
    END IF;

    INSERT INTO document_sequences AS s
        (doc_type, branch_id, year, prefix, last_number, number_padding)
    VALUES
        (p_doc_type, p_branch_id, v_year, COALESCE(NULLIF(p_prefix, ''), p_doc_type), 1,
         COALESCE(p_padding, 6))
    ON CONFLICT ON CONSTRAINT ux_document_sequences
    DO UPDATE SET last_number = s.last_number + 1,
                  prefix = COALESCE(EXCLUDED.prefix, s.prefix),
                  number_padding = COALESCE(p_padding, s.number_padding),
                  updated_at = now()
    RETURNING s.prefix, s.last_number, s.number_padding
        INTO v_prefix, v_number, v_padding;

    RETURN v_prefix || '-' || v_year::text || '-' || lpad(v_number::text, v_padding, '0');
END;
$$;
