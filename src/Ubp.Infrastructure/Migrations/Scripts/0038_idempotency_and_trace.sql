-- 0038 — مفتاح idempotency للمستندات المخزنية + updated_by للجداول القابلة للتعديل
-- لا يُعدَّل أي ترحيل سابق (G1). الجداول stock_movements / stock_lot_movements /
-- document_allocations سجلات ثابتة (append-only) فلا معنى لـ updated_by فيها.

ALTER TABLE stock_documents
    ADD COLUMN idempotency_key varchar(100),
    ADD COLUMN updated_by      uuid REFERENCES users(id) ON DELETE SET NULL;

-- منع إنشاء نفس المستند مرتين عند إعادة المحاولة، لكل فرع على حدة.
CREATE UNIQUE INDEX ux_stock_documents_idempotency
    ON stock_documents (branch_id, idempotency_key)
    WHERE idempotency_key IS NOT NULL;

ALTER TABLE stocktakes
    ADD COLUMN updated_by uuid REFERENCES users(id) ON DELETE SET NULL;

COMMENT ON COLUMN stock_documents.idempotency_key IS
    'مفتاح يرسله العميل؛ يمنع تكرار إنشاء المستند عند إعادة المحاولة (فريد لكل فرع).';
COMMENT ON COLUMN stock_documents.updated_by IS 'آخر مستخدم عدّل المسودة.';
COMMENT ON COLUMN stocktakes.updated_by IS 'آخر مستخدم عدّل الجرد.';
