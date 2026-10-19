-- 0010 — مستخدم التطبيق المحدود (بدون DDL) — كلمة المرور للتطوير فقط
-- تُستبدل في الإنتاج عبر متغيرات البيئة/Secrets ولا تُحفظ في المستودع.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'ubp_app') THEN
        CREATE ROLE ubp_app LOGIN PASSWORD 'ubp_app_dev_password';
    END IF;
END;
$$;

GRANT USAGE ON SCHEMA public TO ubp_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO ubp_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO ubp_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO ubp_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO ubp_app;
