-- 0086 — موديول شكاوى بسيط: تسجيل شكوى على عميل وتحديد الموظف المتسبب
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS complaints (
    id           uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    branch_id    uuid NOT NULL REFERENCES branches(id) ON DELETE RESTRICT,
    customer_id  uuid REFERENCES customers(id) ON DELETE SET NULL,
    employee_id  uuid REFERENCES employees(id) ON DELETE SET NULL,
    doc_date     date NOT NULL DEFAULT current_date,
    description  varchar(1000) NOT NULL,
    status       varchar(10) NOT NULL DEFAULT 'OPEN' CHECK (status IN ('OPEN', 'CLOSED')),
    created_by   uuid REFERENCES users(id) ON DELETE SET NULL,
    created_at   timestamptz NOT NULL DEFAULT now(),
    closed_at    timestamptz,
    closed_by    uuid REFERENCES users(id) ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS ix_complaints_branch_date ON complaints (branch_id, doc_date DESC);
CREATE INDEX IF NOT EXISTS ix_complaints_employee ON complaints (employee_id);
CREATE INDEX IF NOT EXISTS ix_complaints_customer ON complaints (customer_id);

COMMENT ON TABLE complaints IS
    'شكاوى العملاء: العميل + الموظف المتسبب + الوصف + الحالة (OPEN/CLOSED) — 0086.';
