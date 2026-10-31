-- 0051 — الإهلاك بطريقة الوحدات (UNITS): عدد الوحدات المتوقع للأصل
-- لا يُعدَّل أي ترحيل سابق (G1).

ALTER TABLE asset_categories
    ADD COLUMN expected_units numeric(18,2) CHECK (expected_units > 0);

COMMENT ON COLUMN asset_categories.expected_units IS
    'إجمالي الوحدات المتوقع (صفحات/ساعات) لطريقة UNITS: القسط = وحدات الشهر × (التكلفة−التخريدية)/الإجمالي.';
