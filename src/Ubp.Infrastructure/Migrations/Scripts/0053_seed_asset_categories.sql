-- 0053 — تصنيفات أصول افتراضية حتى تعمل شاشة الأصول من أول تشغيل
-- لا يُعدَّل أي ترحيل سابق (G1). الحسابات تُترك فارغة ليضبطها العميل.

INSERT INTO asset_categories (code, name_ar, name_en, depreciation_method,
                              depreciation_frequency, useful_life_months, is_metered)
SELECT v.code, v.name_ar, v.name_en, v.method, v.frequency, v.life, v.metered
FROM (VALUES
    ('MACHINE_METERED', 'آلات بعدّاد (طباعة/تصوير)', 'Metered machines', 'UNITS', 'MONTHLY', 60, true),
    ('FURNITURE', 'أثاث ومفروشات', 'Furniture', 'LINEAR', 'MONTHLY', 60, false),
    ('VEHICLES', 'سيارات ومركبات', 'Vehicles', 'LINEAR', 'ANNUAL', 60, false),
    ('IT_EQUIPMENT', 'أجهزة حاسب وشبكات', 'IT equipment', 'LINEAR', 'MONTHLY', 36, false)
) AS v(code, name_ar, name_en, method, frequency, life, metered)
WHERE NOT EXISTS (SELECT 1 FROM asset_categories c WHERE c.code = v.code);

-- للتصنيف المعدود: الوحدات المتوقعة الافتراضية (تُعدَّل من الشاشة)
UPDATE asset_categories SET expected_units = 100000
WHERE code = 'MACHINE_METERED' AND expected_units IS NULL;
