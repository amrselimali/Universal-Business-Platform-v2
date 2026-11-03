-- 0071 — طريقة تنفيذ الصنف: محجوز ثم يُنفَّذ مستقلًا (الافتراضي) أو يُنفَّذ مباشرة
-- «مباشر» يتجاوز قيود التنفيذ (موظف/موعد/عدّاد/حالة الحجز) ويُرحّل الإيراد والذمم وحركة المخزون فورًا.

ALTER TABLE items ADD COLUMN IF NOT EXISTS execution_mode varchar(20) NOT NULL DEFAULT 'reserved';

ALTER TABLE items DROP CONSTRAINT IF EXISTS ck_items_execution_mode;
ALTER TABLE items ADD CONSTRAINT ck_items_execution_mode
    CHECK (execution_mode IN ('reserved', 'direct'));

COMMENT ON COLUMN items.execution_mode IS
    'reserved = يُحجز ثم يُنفَّذ بشكل مستقل (الافتراضي)، direct = يُنفَّذ مباشرة ويتجاوز قيود التنفيذ.';
