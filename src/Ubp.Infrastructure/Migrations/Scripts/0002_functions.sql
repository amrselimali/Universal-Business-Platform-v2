-- 0002 — دوال مشتركة: تطبيع العربية + تحديث updated_at/version + حماية السجلات
-- ---------------------------------------------------------------------------

-- توحيد الهمزات والتاء المربوطة والياء وإزالة التشكيل والتطويل (يُستخدم في البحث والفهارس)
CREATE OR REPLACE FUNCTION normalize_ar(p_text text)
RETURNS text
LANGUAGE sql
IMMUTABLE
PARALLEL SAFE
AS $$
    SELECT btrim(
        regexp_replace(
            translate(
                lower(coalesce(p_text, '')),
                'أإآٱىةؤئ',
                'اااايهوي'
            ),
            '[ًٌٍَُِّْٰـ]',
            '',
            'g'
        ),
        ' '
    );
$$;

-- إزالة المسافات المتكررة أيضاً (تطبيع كامل للبحث)
CREATE OR REPLACE FUNCTION normalize_search(p_text text)
RETURNS text
LANGUAGE sql
IMMUTABLE
PARALLEL SAFE
AS $$
    SELECT btrim(regexp_replace(normalize_ar(p_text), '\s+', ' ', 'g'));
$$;

-- تحديث updated_at ورفع version تلقائياً مع كل تعديل
CREATE OR REPLACE FUNCTION fn_touch_version()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    NEW.updated_at := now();
    NEW.version := OLD.version + 1;
    RETURN NEW;
END;
$$;

-- منع أي تعديل أو حذف في سجل الأنشطة (للقراءة فقط)
CREATE OR REPLACE FUNCTION fn_audit_log_immutable()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'AUDIT_LOG_IMMUTABLE' USING ERRCODE = '55000';
END;
$$;

-- منع حذف آخر Super Admin نشط
CREATE OR REPLACE FUNCTION fn_protect_last_super_admin()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    remaining integer;
BEGIN
    IF TG_OP = 'DELETE' THEN
        IF OLD.is_super_admin AND OLD.is_active THEN
            SELECT count(*) INTO remaining
            FROM users
            WHERE is_super_admin AND is_active AND id <> OLD.id;
            IF remaining = 0 THEN
                RAISE EXCEPTION 'LAST_SUPER_ADMIN' USING ERRCODE = '23514';
            END IF;
        END IF;
        RETURN OLD;
    END IF;

    IF OLD.is_super_admin AND OLD.is_active
       AND (NOT NEW.is_super_admin OR NOT NEW.is_active) THEN
        SELECT count(*) INTO remaining
        FROM users
        WHERE is_super_admin AND is_active AND id <> OLD.id;
        IF remaining = 0 THEN
            RAISE EXCEPTION 'LAST_SUPER_ADMIN' USING ERRCODE = '23514';
        END IF;
    END IF;
    RETURN NEW;
END;
$$;
