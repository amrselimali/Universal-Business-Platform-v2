-- 0008 — الفهارس: بحث عربي trigram + ترقيم سريع + مفاتيح أجنبية
-- ----------------------------------------------------------------

CREATE INDEX ix_branches_search ON branches USING gin (search_text gin_trgm_ops);
CREATE INDEX ix_warehouses_search ON warehouses USING gin (search_text gin_trgm_ops);
CREATE INDEX ix_users_search ON users USING gin (search_text gin_trgm_ops);

CREATE INDEX ix_branches_active ON branches (is_active, id);
CREATE INDEX ix_warehouses_active ON warehouses (is_active, id);
CREATE INDEX ix_users_active ON users (is_active, id);
CREATE INDEX ix_roles_active ON roles (is_active, id);

CREATE INDEX ix_warehouse_branches_branch ON warehouse_branches (branch_id);
CREATE INDEX ix_user_branches_branch ON user_branches (branch_id);
CREATE INDEX ix_user_branches_user ON user_branches (user_id);
CREATE INDEX ix_user_warehouses_warehouse ON user_warehouses (warehouse_id);
CREATE INDEX ix_user_warehouses_user ON user_warehouses (user_id);

CREATE INDEX ix_role_permissions_role ON role_permissions (role_id);
CREATE INDEX ix_user_roles_role ON user_roles (role_id);
CREATE INDEX ix_user_permissions_user ON user_permissions (user_id, screen_code);

CREATE INDEX ix_refresh_tokens_user ON refresh_tokens (user_id, expires_at DESC);
CREATE INDEX ix_user_sessions_user ON user_sessions (user_id, expires_at DESC);
CREATE INDEX ix_user_sessions_active ON user_sessions (expires_at) WHERE revoked_at IS NULL;

-- audit_log: ترقيم Keyset وفلاتر الشاشة
CREATE INDEX ix_audit_user_keyset ON audit_log (user_id, id DESC);
CREATE INDEX ix_audit_branch_keyset ON audit_log (branch_id, id DESC);
CREATE INDEX ix_audit_table_record ON audit_log (table_name, record_id);
CREATE INDEX ix_audit_created ON audit_log (created_at DESC);
CREATE INDEX ix_audit_action_keyset ON audit_log (action, id DESC);

CREATE INDEX ix_custom_fields_entity ON custom_field_definitions (entity_name, is_active, sort_order);
CREATE INDEX ix_qa_results_run ON qa_test_results (run_id, id);
CREATE INDEX ix_qa_runs_created ON qa_test_runs (created_at DESC);
