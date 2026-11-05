-- 0076 — عدّ نقدية الوردية لكل خزينة (مسودة حتى ترحيل المحاسب) + حساب زيادة/عجز لكل خزينة
-- ---------------------------------------------------------------------------
-- 1) عند فتح الوردية تُحفظ لقطة افتتاح كل خزينة متابَعة (من الحسابات المرحّلة +
--    أرصدة الشيفتات غير المرحلة المسجلة يدوياً عند الإغلاق).
-- 2) عند الإغلاق تُحسب المتوقع لكل خزينة ويُسجَّل النقد الفعلي والفرق كمسودة
--    (status = 'draft') — لا يُرحَّل أي قيد زيادة/عجز إلا باعتماد المحاسب.
-- 3) شاشة الخزائن: حساب زيادة/عجز خاص بكل خزينة يُستخدم عند الترحيل (بديلاً عن
--    الافتراضي العام cash_over_short).

CREATE TABLE IF NOT EXISTS shift_cash_counts (
    id               uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    shift_id         uuid NOT NULL REFERENCES cashier_shifts(id) ON DELETE CASCADE,
    treasury_id      uuid NOT NULL REFERENCES treasuries(id) ON DELETE RESTRICT,
    opening_cash     numeric(18,2) NOT NULL DEFAULT 0,
    expected_cash    numeric(18,2) NOT NULL DEFAULT 0,
    counted_cash     numeric(18,2),
    variance         numeric(18,2),
    status           varchar(10) NOT NULL DEFAULT 'open'
                     CHECK (status IN ('open','draft','posted')),
    posted_at        timestamptz,
    posted_by        uuid REFERENCES users(id) ON DELETE SET NULL,
    journal_entry_id uuid REFERENCES journal_entries(id) ON DELETE SET NULL,
    created_at       timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ux_shift_cash_counts UNIQUE (shift_id, treasury_id)
);

CREATE INDEX IF NOT EXISTS ix_shift_cash_counts_shift
    ON shift_cash_counts (shift_id, status);

COMMENT ON TABLE shift_cash_counts IS
    'عدّ النقدية لكل خزينة في الوردية: لقطة الافتتاح عند الفتح، والفعلي/الفرق مسودة عند الإغلاق حتى يعتمدها المحاسب.';

ALTER TABLE treasuries
    ADD COLUMN IF NOT EXISTS over_short_account_id uuid REFERENCES accounts(id) ON DELETE RESTRICT;

COMMENT ON COLUMN treasuries.over_short_account_id IS
    'حساب ترحيل زيادة/عجز هذه الخزينة عند اعتماد المحاسب (0076) — عند تركه فارغاً يُستخدم cash_over_short العام';
