-- 0028 — المرحلة 4C: أنواع مصدر قيود الخزينة + فهارس تقارير السندات والورديات
-- -----------------------------------------------------------------------------

-- القيود الناتجة عن سندات الخزينة وإغلاق الورديات لها نوعا مصدر صريحان
ALTER TABLE journal_entries DROP CONSTRAINT IF EXISTS journal_entries_source_type_check;
ALTER TABLE journal_entries
    ADD CONSTRAINT ck_journal_entries_source_type
    CHECK (source_type IN ('manual', 'stock_document', 'stocktake', 'opening', 'invoice',
                           'voucher', 'cash_shift'));

-- حركة نقدية الوردية: سندات مرحّلة على الخزينة داخل زمن الوردية
CREATE INDEX IF NOT EXISTS ix_vouchers_treasury_posted
    ON vouchers (treasury_id, posted_at) WHERE status = 'posted';
CREATE INDEX IF NOT EXISTS ix_vouchers_status
    ON vouchers (branch_id, status, doc_date DESC);

-- كشف تخصيصات الفاتورة (من السند إلى المستند)
CREATE INDEX IF NOT EXISTS ix_allocations_target
    ON document_allocations (target_document_id);

-- التخصيص يحدّث المبلغ المخصص على فاتورة مرحّلة (N12)؛ الحماية تبقى على كل حقل آخر:
-- يُسمح بتغيير allocated_total وحده، وأي تغيير آخر على مستند غير مسودة يبقى مرفوضاً.
CREATE OR REPLACE FUNCTION fn_commercial_doc_guard()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        IF OLD.status <> 'draft' THEN
            RAISE EXCEPTION 'POSTED_DOCUMENT_IMMUTABLE' USING ERRCODE = '23514';
        END IF;
        RETURN OLD;
    END IF;

    IF OLD.status <> 'draft' THEN
        IF (to_jsonb(NEW) - 'allocated_total' - 'version' - 'updated_at' - 'updated_by')
           = (to_jsonb(OLD) - 'allocated_total' - 'version' - 'updated_at' - 'updated_by') THEN
            RETURN NEW;
        END IF;
        RAISE EXCEPTION 'POSTED_DOCUMENT_IMMUTABLE' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END;
$$;

ALTER TABLE commercial_documents
    DROP CONSTRAINT IF EXISTS ck_commercial_allocated_within_total;
ALTER TABLE commercial_documents
    ADD CONSTRAINT ck_commercial_allocated_within_total
    CHECK (allocated_total <= total);

