-- 0011 — المرحلة 2أ: إعداد الرصيد السالب + ترقيم المستندات
-- ---------------------------------------------------------
-- الصلاحيات: ALTER DEFAULT PRIVILEGES في 0010 تمنح ubp_app صلاحيات الجداول الجديدة تلقائياً،
-- والدوال تُنفَّذ افتراضياً لكل الأدوار، فلا حاجة لـ GRANT هنا.

-- 1) السماح بالرصيد السالب (يُعدَّل من شاشة الإعدادات العامة، ويقرؤه محرك ترحيل المخزون لاحقاً)
ALTER TABLE app_settings
    ADD COLUMN allow_negative_stock boolean NOT NULL DEFAULT false;

COMMENT ON COLUMN app_settings.allow_negative_stock IS
    'false = يُرفض أي صرف أو تحويل يجعل رصيد الصنف في المخزن سالباً';

-- 2) عدّاد ترقيم المستندات لكل (نوع مستند، فرع، سنة)
CREATE TABLE document_sequences (
    id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    doc_type    varchar(30)  NOT NULL,
    branch_id   uuid         NOT NULL REFERENCES branches(id) ON DELETE RESTRICT,
    year        smallint     NOT NULL,
    prefix      varchar(10),
    last_number integer      NOT NULL DEFAULT 0,
    created_at  timestamptz  NOT NULL DEFAULT now(),
    updated_at  timestamptz  NOT NULL DEFAULT now(),
    CONSTRAINT ux_document_sequences UNIQUE (doc_type, branch_id, year),
    CONSTRAINT ck_document_sequences_type CHECK (doc_type ~ '^[A-Z][A-Z0-9_]*$'),
    CONSTRAINT ck_document_sequences_year CHECK (year BETWEEN 2000 AND 2100),
    CONSTRAINT ck_document_sequences_last CHECK (last_number >= 0)
);

-- 3) الرقم التالي: INSERT ... ON CONFLICT DO UPDATE يقفل الصف، فالطلبات المتزامنة تأخذ أرقاماً مختلفة.
--    تُستدعى داخل transaction حفظ المستند نفسه، فلو فشل الحفظ يرجع الرقم ولا تحدث فجوات.
CREATE OR REPLACE FUNCTION next_document_number(
    p_doc_type  varchar,
    p_branch_id uuid,
    p_doc_date  date,
    p_prefix    varchar DEFAULT NULL
) RETURNS varchar
LANGUAGE plpgsql
AS $$
DECLARE
    v_year   smallint := EXTRACT(YEAR FROM p_doc_date)::smallint;
    v_prefix varchar;
    v_number integer;
BEGIN
    INSERT INTO document_sequences AS s (doc_type, branch_id, year, prefix, last_number)
    VALUES (p_doc_type, p_branch_id, v_year, COALESCE(p_prefix, p_doc_type), 1)
    ON CONFLICT (doc_type, branch_id, year)
    DO UPDATE SET last_number = s.last_number + 1,
                  updated_at  = now()
    RETURNING s.prefix, s.last_number INTO v_prefix, v_number;

    RETURN COALESCE(v_prefix, p_doc_type) || '-' || v_year::text || '-' || lpad(v_number::text, 6, '0');
END;
$$;
