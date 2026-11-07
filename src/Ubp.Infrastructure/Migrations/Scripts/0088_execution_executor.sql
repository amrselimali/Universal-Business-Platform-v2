-- 0088 — منفّذ الجلسة: يُختار من نافذة «تنفيذ الخدمة» ويُحفظ على سطر التنفيذ (عمود مستقل)
ALTER TABLE booking_execution_lines ADD COLUMN IF NOT EXISTS employee_id uuid
    REFERENCES employees(id) ON DELETE SET NULL;
CREATE INDEX IF NOT EXISTS ix_booking_execution_lines_employee
    ON booking_execution_lines (employee_id);
COMMENT ON COLUMN booking_execution_lines.employee_id IS
    'الموظف المنفّذ المختار من نافذة تنفيذ الخدمة (منفّذ الجلسة) — 0088.';
