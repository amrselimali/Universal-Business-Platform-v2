-- 0067 — حساب عمولة مستقل لكل طريقة تحصيل
-- يُحدَّد عند تكويد طريقة الدفع في شاشة الخزائن، ويُرحَّل إليه عمولة(رسوم) الطريقة
-- عند التحصيل من نقطة البيع؛ وإذا تُرك فارغًا يُستخدم حساب payment_fees العام (توافق خلفي).

ALTER TABLE treasury_methods
    ADD COLUMN IF NOT EXISTS fee_account_id uuid REFERENCES accounts(id) ON DELETE RESTRICT;

COMMENT ON COLUMN treasury_methods.fee_account_id IS
    'حساب عمولة/رسوم طريقة التحصيل؛ يُرحَّل إليه صافي الرسوم، وإلا يُسقط على payment_fees العام.';
