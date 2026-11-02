-- 0056 — دور إداري ووظيفة للموظف
ALTER TABLE employees ADD COLUMN IF NOT EXISTS job_title varchar(100);
ALTER TABLE employees ADD COLUMN IF NOT EXISTS role_id uuid REFERENCES roles(id) ON DELETE SET NULL;

CREATE INDEX IF NOT EXISTS ix_employees_role ON employees (role_id);
