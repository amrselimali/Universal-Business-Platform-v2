-- 0006 — مشغلات التزامن والحماية
-- ---------------------------------

CREATE TRIGGER trg_touch_app_settings BEFORE UPDATE ON app_settings
FOR EACH ROW EXECUTE FUNCTION fn_touch_version();
CREATE TRIGGER trg_touch_branches BEFORE UPDATE ON branches
FOR EACH ROW EXECUTE FUNCTION fn_touch_version();
CREATE TRIGGER trg_touch_warehouses BEFORE UPDATE ON warehouses
FOR EACH ROW EXECUTE FUNCTION fn_touch_version();
CREATE TRIGGER trg_touch_currencies BEFORE UPDATE ON currencies
FOR EACH ROW EXECUTE FUNCTION fn_touch_version();
CREATE TRIGGER trg_touch_custom_fields BEFORE UPDATE ON custom_field_definitions
FOR EACH ROW EXECUTE FUNCTION fn_touch_version();
CREATE TRIGGER trg_touch_users BEFORE UPDATE ON users
FOR EACH ROW EXECUTE FUNCTION fn_touch_version();
CREATE TRIGGER trg_touch_roles BEFORE UPDATE ON roles
FOR EACH ROW EXECUTE FUNCTION fn_touch_version();

CREATE TRIGGER trg_audit_immutable_update BEFORE UPDATE ON audit_log
FOR EACH ROW EXECUTE FUNCTION fn_audit_log_immutable();
CREATE TRIGGER trg_audit_immutable_delete BEFORE DELETE ON audit_log
FOR EACH ROW EXECUTE FUNCTION fn_audit_log_immutable();

CREATE TRIGGER trg_protect_last_super_admin BEFORE UPDATE OR DELETE ON users
FOR EACH ROW EXECUTE FUNCTION fn_protect_last_super_admin();

-- منع الحذف المباشر لفرع أو مخزن ما زال مرتبطاً (الواجهة تحوّل الحذف إلى تعطيل)
CREATE OR REPLACE FUNCTION fn_block_branch_delete()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF EXISTS (SELECT 1 FROM user_branches WHERE branch_id = OLD.id)
       OR EXISTS (SELECT 1 FROM warehouse_branches WHERE branch_id = OLD.id) THEN
        RAISE EXCEPTION 'BRANCH_IN_USE' USING ERRCODE = '23503';
    END IF;
    RETURN OLD;
END;
$$;

CREATE OR REPLACE FUNCTION fn_block_warehouse_delete()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF EXISTS (SELECT 1 FROM user_warehouses WHERE warehouse_id = OLD.id)
       OR EXISTS (SELECT 1 FROM warehouse_branches WHERE warehouse_id = OLD.id) THEN
        RAISE EXCEPTION 'WAREHOUSE_IN_USE' USING ERRCODE = '23503';
    END IF;
    RETURN OLD;
END;
$$;

CREATE TRIGGER trg_block_branch_delete BEFORE DELETE ON branches
FOR EACH ROW EXECUTE FUNCTION fn_block_branch_delete();
CREATE TRIGGER trg_block_warehouse_delete BEFORE DELETE ON warehouses
FOR EACH ROW EXECUTE FUNCTION fn_block_warehouse_delete();
