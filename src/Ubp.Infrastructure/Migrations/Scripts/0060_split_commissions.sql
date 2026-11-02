-- 0060 — عمولة تسجيل الحجز وعمولة تنفيذ الجلسة منفصلتان
ALTER TABLE employee_commissions
    ADD COLUMN IF NOT EXISTS user_id uuid REFERENCES users(id) ON DELETE SET NULL;
ALTER TABLE employee_commissions
    ADD COLUMN IF NOT EXISTS commission_kind varchar(20) NOT NULL DEFAULT 'booking'
    CHECK (commission_kind IN ('booking', 'execution'));

ALTER TABLE employee_commissions ALTER COLUMN employee_id DROP NOT NULL;

ALTER TABLE employee_commissions DROP CONSTRAINT IF EXISTS ux_commissions;

CREATE UNIQUE INDEX IF NOT EXISTS ux_commissions_line_kind
    ON employee_commissions (document_line_id, commission_kind, period_month);
CREATE INDEX IF NOT EXISTS ix_commissions_user_period
    ON employee_commissions (user_id, period_month);

COMMENT ON COLUMN employee_commissions.commission_kind IS
    'booking = من سجّل الحجز، execution = من نفّذ الجلسة.';
