-- 0090 — اللون العام للبرنامج قابل للتحديد من شاشة إعدادات الشركة
ALTER TABLE app_settings ADD COLUMN IF NOT EXISTS theme_color varchar(20) NOT NULL
    DEFAULT '#0F766E';
COMMENT ON COLUMN app_settings.theme_color IS
    'اللون العام (Primary) للبرنامج بصيغة #RRGGBB — يُطبَّق على كل الواجهة (0090).';
