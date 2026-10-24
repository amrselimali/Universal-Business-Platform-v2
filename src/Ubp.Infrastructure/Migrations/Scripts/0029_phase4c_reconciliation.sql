-- 0029 — المرحلة 4C-ب: تسوية الحسابات الوسيطة وإعادة فتح الوردية
-- -----------------------------------------------------------------------------

-- تسوية البطاقات/المحافظ/الشيكات: البحث عن الحساب الوسيط بسرعة
CREATE INDEX IF NOT EXISTS ix_treasury_methods_clearing
    ON treasury_methods (clearing_account_id) WHERE clearing_account_id IS NOT NULL;

-- أثر إعادة فتح الوردية (مع إبقاء الحالة 'open' لضمان وردية واحدة لكل خزينة)
ALTER TABLE cashier_shifts ADD COLUMN IF NOT EXISTS reopened_at timestamptz;
ALTER TABLE cashier_shifts ADD COLUMN IF NOT EXISTS reopen_reason varchar(300);

COMMENT ON COLUMN cashier_shifts.reopen_reason IS
    'سبب إعادة فتح وردية مغلقة — يُسجَّل مع عكس قيد الفرق';
