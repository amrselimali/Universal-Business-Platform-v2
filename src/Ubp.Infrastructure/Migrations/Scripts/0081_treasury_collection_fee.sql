-- 0081 — عمولة تحصيل بنسبة مئوية لكل خزينة + حساب ترحيل العمولة
-- ---------------------------------------------------------------------------
-- تُحسب عند إغلاق الوردية: مجموع تحصيلات الخزينة × النسبة = مصروف عمولة
-- يُخصم من الخزينة ويُرحَّل على الحساب المربوط عند اعتماد المحاسب.
-- (العمولة القديمة في طرق الدفع تبقى كما هي دون تغيير.)

ALTER TABLE treasuries
    ADD COLUMN IF NOT EXISTS collection_fee_percent numeric(5,2) NOT NULL DEFAULT 0
        CHECK (collection_fee_percent BETWEEN 0 AND 100);
ALTER TABLE treasuries
    ADD COLUMN IF NOT EXISTS collection_fee_account_id uuid REFERENCES accounts(id) ON DELETE RESTRICT;

COMMENT ON COLUMN treasuries.collection_fee_percent IS
    'نسبة عمولة التحصيل المخصومة من تحصيلات الخزينة في الوردية (0081)';
COMMENT ON COLUMN treasuries.collection_fee_account_id IS
    'حساب ترحيل عمولة التحصيل عند اعتماد الوردية (0081)';
