-- 0082 — عمولة تسليم النقدية: تُسجَّل على حساب عمولة الخزينة وتُضاف لمصروفات الوردية.
ALTER TABLE cash_handovers
    ADD COLUMN IF NOT EXISTS commission_amount numeric(18,2) NOT NULL DEFAULT 0
        CHECK (commission_amount >= 0);

COMMENT ON COLUMN cash_handovers.commission_amount IS
    'عمولة تسليم النقدية (تُرحَّل على حساب عمولة تحصيل الخزينة) — 0082';
