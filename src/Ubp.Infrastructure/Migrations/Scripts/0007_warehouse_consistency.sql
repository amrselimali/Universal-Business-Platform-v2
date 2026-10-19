-- 0007 — حارس تناسق: مخازن المستخدم يجب أن تكون تابعة لفروع المستخدم
-- -----------------------------------------------------------------

CREATE OR REPLACE FUNCTION fn_user_is_admin(p_user uuid)
RETURNS boolean LANGUAGE sql STABLE AS $$
    SELECT coalesce((SELECT is_admin FROM users WHERE id = p_user), false);
$$;

CREATE OR REPLACE FUNCTION fn_user_has_branches(p_user uuid)
RETURNS boolean LANGUAGE sql STABLE AS $$
    SELECT EXISTS (SELECT 1 FROM user_branches WHERE user_id = p_user);
$$;

CREATE OR REPLACE FUNCTION fn_check_user_warehouse_coverage(p_user uuid, p_exclude_branch uuid DEFAULT NULL)
RETURNS void LANGUAGE plpgsql AS $$
DECLARE
    bad_names text;
BEGIN
    IF fn_user_is_admin(p_user) THEN
        RETURN;
    END IF;

    SELECT string_agg(w.name_ar, ', ') INTO bad_names
    FROM user_warehouses uw
    JOIN warehouses w ON w.id = uw.warehouse_id
    WHERE uw.user_id = p_user
      AND NOT EXISTS (
          SELECT 1
          FROM warehouse_branches wb
          JOIN user_branches ub ON ub.branch_id = wb.branch_id
          WHERE wb.warehouse_id = uw.warehouse_id
            AND ub.user_id = p_user
            AND (p_exclude_branch IS NULL OR ub.branch_id <> p_exclude_branch)
      );

    IF bad_names IS NOT NULL THEN
        RAISE EXCEPTION 'USER_WAREHOUSE_COVERAGE: %', bad_names USING ERRCODE = '23514';
    END IF;
END;
$$;

CREATE OR REPLACE FUNCTION fn_user_warehouse_guard()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM users WHERE id = NEW.user_id) THEN
        RETURN NEW;
    END IF;
    IF fn_user_is_admin(NEW.user_id) THEN
        RETURN NEW;
    END IF;
    IF NOT fn_user_has_branches(NEW.user_id) THEN
        RAISE EXCEPTION 'USER_WAREHOUSE_COVERAGE: %',
            coalesce((SELECT name_ar FROM warehouses WHERE id = NEW.warehouse_id), NEW.warehouse_id::text)
            USING ERRCODE = '23514';
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM warehouse_branches wb
        JOIN user_branches ub ON ub.branch_id = wb.branch_id
        WHERE wb.warehouse_id = NEW.warehouse_id AND ub.user_id = NEW.user_id
    ) THEN
        RAISE EXCEPTION 'USER_WAREHOUSE_COVERAGE: %',
            coalesce((SELECT name_ar FROM warehouses WHERE id = NEW.warehouse_id), NEW.warehouse_id::text)
            USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END;
$$;

CREATE OR REPLACE FUNCTION fn_user_branch_guard()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    -- تجاهل الحذف المتسلسل عند حذف المستخدم نفسه
    IF NOT EXISTS (SELECT 1 FROM users WHERE id = OLD.user_id) THEN
        RETURN OLD;
    END IF;
    IF fn_user_is_admin(OLD.user_id) THEN
        RETURN OLD;
    END IF;
    PERFORM fn_check_user_warehouse_coverage(OLD.user_id, OLD.branch_id);
    RETURN OLD;
END;
$$;

CREATE OR REPLACE FUNCTION fn_warehouse_branch_guard()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    u record;
BEGIN
    IF NOT EXISTS (SELECT 1 FROM warehouses WHERE id = OLD.warehouse_id)
       OR NOT EXISTS (SELECT 1 FROM branches WHERE id = OLD.branch_id) THEN
        RETURN OLD;
    END IF;

    FOR u IN
        SELECT DISTINCT ub.user_id
        FROM user_branches ub
        JOIN warehouse_branches wb ON wb.branch_id = ub.branch_id
        WHERE wb.warehouse_id = OLD.warehouse_id
          AND ub.user_id IN (SELECT user_id FROM user_warehouses WHERE warehouse_id = OLD.warehouse_id)
    LOOP
        PERFORM fn_check_user_warehouse_coverage(u.user_id, OLD.branch_id);
    END LOOP;
    RETURN OLD;
END;
$$;

CREATE TRIGGER trg_user_warehouse_guard BEFORE INSERT OR UPDATE ON user_warehouses
FOR EACH ROW EXECUTE FUNCTION fn_user_warehouse_guard();
CREATE TRIGGER trg_user_branch_guard BEFORE DELETE ON user_branches
FOR EACH ROW EXECUTE FUNCTION fn_user_branch_guard();
CREATE TRIGGER trg_warehouse_branch_guard BEFORE DELETE ON warehouse_branches
FOR EACH ROW EXECUTE FUNCTION fn_warehouse_branch_guard();
