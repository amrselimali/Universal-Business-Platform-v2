-- 0061 — إلغاء شاشة عمولات الموظفين وكل أثرها
DROP TABLE IF EXISTS employee_commissions;
ALTER TABLE app_settings DROP COLUMN IF EXISTS commission_basis;
