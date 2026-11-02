-- 0063 — تنظيف بقايا شاشة العمولات من الصلاحيات وفلاتر التقارير
DELETE FROM role_permissions WHERE screen_code = 'SALES_COMMISSIONS';
DELETE FROM user_permissions WHERE screen_code = 'SALES_COMMISSIONS';
DELETE FROM saved_report_filters WHERE report_key = 'hr.commissions';