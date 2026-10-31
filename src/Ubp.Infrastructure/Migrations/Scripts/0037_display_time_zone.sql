-- 0037 — منطقة العرض الزمنية في إعدادات النظام
-- التخزين يبقى UTC في كل الجداول؛ هذا العمود يخص العرض فقط.

ALTER TABLE app_settings
    ADD COLUMN time_zone varchar(64) NOT NULL DEFAULT 'Africa/Cairo';

COMMENT ON COLUMN app_settings.time_zone IS
    'منطقة IANA لعرض التواريخ والأوقات (مثال: Africa/Cairo). التخزين يبقى UTC.';
