-- 0068 — أرقام تواصل العميل/المورد: رقم ثانٍ ورقم واتساب مخصص
-- يُستخدم رقم الواتساب المحدد أولاً، ثم الهاتف الأساسي، ثم الرقم الثاني.

ALTER TABLE customers ADD COLUMN IF NOT EXISTS phone2 varchar(30);
ALTER TABLE customers ADD COLUMN IF NOT EXISTS whatsapp_phone varchar(30);
ALTER TABLE suppliers ADD COLUMN IF NOT EXISTS phone2 varchar(30);
ALTER TABLE suppliers ADD COLUMN IF NOT EXISTS whatsapp_phone varchar(30);

COMMENT ON COLUMN customers.whatsapp_phone IS
    'رقم الواتساب المخصص للتواصل (اختياري)؛ عند غيابه يُستخدم الهاتف الأساسي ثم الثاني.';
COMMENT ON COLUMN suppliers.whatsapp_phone IS
    'رقم الواتساب المخصص للتواصل (اختياري)؛ عند غيابه يُستخدم الهاتف الأساسي ثم الثاني.';
