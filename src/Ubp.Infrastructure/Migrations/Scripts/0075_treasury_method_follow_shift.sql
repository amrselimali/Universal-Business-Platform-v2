-- 0075 — متابعة طرق الدفع في الوردية
-- ---------------------------------------------------------------------------
-- كل طريقة دفع لها خيار «يتم متابعتها في الوردية»: الخزائن وطرق الدفع المعلَّمة
-- فقط تظهر أرصدتها في شاشة فتح الوردية (من الحسابات المرحّلة + الحركات غير المرحّلة).

ALTER TABLE treasury_methods
    ADD COLUMN IF NOT EXISTS follow_in_shift boolean NOT NULL DEFAULT true;

COMMENT ON COLUMN treasury_methods.follow_in_shift IS
    'طريقة الدفع متابَعة في الوردية: تظهر خزينتها وأرصدتها في شاشة فتح الوردية (0075)';

CREATE INDEX IF NOT EXISTS ix_treasury_methods_follow
    ON treasury_methods (treasury_id) WHERE follow_in_shift AND is_active;
