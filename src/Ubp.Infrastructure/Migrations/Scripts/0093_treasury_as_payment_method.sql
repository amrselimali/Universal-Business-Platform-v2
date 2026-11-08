-- 0093 — الخزائن هي طرق الدفع: إلغاء آلية طرق الدفع من الشاشات
-- ---------------------------------------------------------------------------
-- 1) متابعة الوردية تنتقل إلى الخزينة نفسها (treasuries.follow_in_shift)، ورصيد
--    الخزينة هو ما يظهر كأول مدة عند فتح الوردية.
-- 2) عمولة التحصيل المحملة على العميل تبقى على الخزينة (collection_fee_*).
--    عمولة التحصيل المحملة من مزود الخدمة تنتقل من طريقة الدفع إلى الخزينة
--    (provider_fee_*).
-- 3) تُنشأ «طريقة افتراضية» لكل خزينة لتبقى السندات ومستندات المصروفات القديمة
--    المرتبطة بـ treasury_method_id صالحة، دون إظهار طرق الدفع في أي شاشة.
-- 4) مفاتيح حسابات افتراضية جديدة في شاشة إعدادات الحسابات.

ALTER TABLE treasuries
    ADD COLUMN IF NOT EXISTS follow_in_shift boolean NOT NULL DEFAULT true;
COMMENT ON COLUMN treasuries.follow_in_shift IS
    'الخزينة يتم متابعتها في الوردية: يظهر رصيدها كأول مدة عند فتح الوردية (0093)';

ALTER TABLE treasuries
    ADD COLUMN IF NOT EXISTS provider_fee_percent numeric(5,2) NOT NULL DEFAULT 0
        CHECK (provider_fee_percent BETWEEN 0 AND 100);
ALTER TABLE treasuries
    ADD COLUMN IF NOT EXISTS provider_fee_amount numeric(18,2) NOT NULL DEFAULT 0
        CHECK (provider_fee_amount >= 0);
ALTER TABLE treasuries
    ADD COLUMN IF NOT EXISTS provider_fee_account_id uuid
        REFERENCES accounts(id) ON DELETE RESTRICT;

COMMENT ON COLUMN treasuries.collection_fee_percent IS
    'عمولة التحصيل المحملة على العميل (%) — 0081/0093';
COMMENT ON COLUMN treasuries.collection_fee_account_id IS
    'حساب ترحيل عمولة التحصيل المحملة على العميل — 0081/0093';
COMMENT ON COLUMN treasuries.provider_fee_percent IS
    'عمولة التحصيل المحملة من مزود الخدمة (%) — 0093';
COMMENT ON COLUMN treasuries.provider_fee_amount IS
    'عمولة التحصيل المحملة من مزود الخدمة (مبلغ ثابت) — 0093';
COMMENT ON COLUMN treasuries.provider_fee_account_id IS
    'حساب ترحيل عمولة التحصيل المحملة من مزود الخدمة — 0093';

-- (1) ترحيل علامة المتابعة من طرق الدفع إلى الخزائن ثم توحيدها على الطرق.
UPDATE treasuries t
SET follow_in_shift = flag.follow
FROM (
    SELECT treasury_id, bool_or(follow_in_shift) AS follow
    FROM treasury_methods
    WHERE is_active
    GROUP BY treasury_id
) flag
WHERE flag.treasury_id = t.id;

UPDATE treasury_methods m
SET follow_in_shift = t.follow_in_shift
FROM treasuries t
WHERE t.id = m.treasury_id;

-- (2) ترحيل رسوم/عمولة مزود الخدمة من طريقة الدفع الأساسية إلى الخزينة.
UPDATE treasuries t
SET provider_fee_percent = src.fee_percent,
    provider_fee_amount = src.fee_fixed,
    provider_fee_account_id = src.fee_account_id
FROM (
    SELECT DISTINCT ON (treasury_id) treasury_id, fee_percent, fee_fixed, fee_account_id
    FROM treasury_methods
    WHERE is_active
    ORDER BY treasury_id, code
) src
WHERE src.treasury_id = t.id
  AND (src.fee_percent > 0 OR src.fee_fixed > 0 OR src.fee_account_id IS NOT NULL);

-- (3) طريقة افتراضية لكل خزينة (تُستخدم داخلياً فقط ولا تظهر في أي شاشة).
INSERT INTO treasury_methods
    (treasury_id, code, name_ar, name_en, method_type, follow_in_shift,
     fee_percent, fee_fixed, fee_account_id)
SELECT t.id, 'DEFAULT', t.name_ar, t.name_en,
       CASE t.treasury_type
           WHEN 'cash' THEN 'cash'
           WHEN 'wallet' THEN 'wallet'
           ELSE 'transfer'
       END,
       t.follow_in_shift, t.provider_fee_percent, t.provider_fee_amount,
       t.provider_fee_account_id
FROM treasuries t
WHERE NOT EXISTS (
    SELECT 1 FROM treasury_methods m
    WHERE m.treasury_id = t.id AND m.code = 'DEFAULT'
);

-- (4) مفاتيح الحسابات الافتراضية الجديدة (تُدار من شاشة إعدادات الحسابات).
INSERT INTO posting_default_types
    (key, module, party_type, is_required, sort_order, label_ar, label_en)
VALUES
    ('employee_advances', 'expenses', NULL, false, 450,
     'حساب تحميل سلف الموظفين', 'Employee advances account'),
    ('cash_clearing', 'treasury', NULL, false, 460,
     'الحساب الوسيط لنقدية الفرع', 'Branch cash clearing account'),
    ('cash_handover_commission', 'treasury', NULL, false, 470,
     'عمولة تسليم النقدية', 'Cash handover commission'),
    ('operating_expense', 'expenses', NULL, false, 480,
     'حساب المصروف التشغيلي الافتراضي', 'Default operating expense account')
ON CONFLICT (key) DO NOTHING;
