-- 0009 — البيانات الأولية الضامنة للتشغيل
-- ----------------------------------------
-- ملاحظة: كلمة مرور الأدمن الأول تُنشأ هنا بقيمة افتراضية موثّقة ويجب تغييرها بعد أول دخول.
-- يمكن ضبط كلمة مرور مختلفة عبر Bootstrap:SuperAdminPassword في أول تشغيل (DataSeeder).

INSERT INTO app_settings (company_name, default_language, date_format, time_format, number_format)
SELECT 'Universal Business Platform', 'ar', 'dd/MM/yyyy', 'HH:mm', '#,##0.00'
WHERE NOT EXISTS (SELECT 1 FROM app_settings);

INSERT INTO currencies (currency_code, name_ar, name_en, symbol, decimals, is_base)
VALUES ('EGP', 'الجنيه المصري', 'Egyptian Pound', 'ج.م', 2, true)
ON CONFLICT DO NOTHING;

INSERT INTO currencies (currency_code, name_ar, name_en, symbol, decimals, is_base)
VALUES ('USD', 'الدولار الأمريكي', 'US Dollar', '$', 2, false)
ON CONFLICT DO NOTHING;

INSERT INTO roles (code, name_ar, name_en)
VALUES ('BRANCH_MANAGER', 'مدير فرع', 'Branch Manager'),
       ('ACCOUNTANT', 'محاسب', 'Accountant'),
       ('AUDITOR', 'مراجع', 'Auditor')
ON CONFLICT (code) DO NOTHING;

INSERT INTO branches (code, name_ar, name_en, phone)
SELECT 'MAIN', 'الفرع الرئيسي', 'Main Branch', NULL
WHERE NOT EXISTS (SELECT 1 FROM branches);

INSERT INTO warehouses (code, name_ar, name_en)
SELECT 'WH-MAIN', 'المخزن الرئيسي', 'Main Warehouse'
WHERE NOT EXISTS (SELECT 1 FROM warehouses);

INSERT INTO warehouse_branches (warehouse_id, branch_id)
SELECT w.id, b.id
FROM warehouses w, branches b
WHERE w.code = 'WH-MAIN' AND b.code = 'MAIN'
ON CONFLICT DO NOTHING;

INSERT INTO users (name, username, password_hash, is_active, language, is_admin, is_super_admin)
SELECT 'مدير النظام', 'admin', crypt('Admin@123', gen_salt('bf', 12)), true, 'ar', true, true
WHERE NOT EXISTS (SELECT 1 FROM users WHERE is_super_admin);
