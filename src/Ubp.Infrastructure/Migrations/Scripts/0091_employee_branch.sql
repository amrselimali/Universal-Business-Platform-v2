-- 0091 — ربط الموظف بفرع (أو الشركة عند ترك الفرع فارغًا)
ALTER TABLE employees ADD COLUMN IF NOT EXISTS branch_id uuid
    REFERENCES branches(id) ON DELETE RESTRICT;
CREATE INDEX IF NOT EXISTS ix_employees_branch ON employees (branch_id);
COMMENT ON COLUMN employees.branch_id IS
    'فرع الموظف؛ NULL = موظف على مستوى الشركة (0091).';