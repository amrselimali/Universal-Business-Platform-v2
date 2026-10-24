-- 0023 — تحسين فهرس كشف الأستاذ: فحص index-only لتجميع مدين/دائن الفترة
-- -------------------------------------------------------------------------
-- الاستعلام يجمع debit/credit مع فلترة account_id + entry_date + branch_id،
-- فإضافة هذه الأعمدة إلى الفهرس تمنع الرجوع إلى الجدول (heap) لكل بند.

DROP INDEX IF EXISTS ix_journal_lines_ledger;

CREATE INDEX ix_journal_lines_ledger
    ON journal_entry_lines (account_id, entry_date, id)
    INCLUDE (debit, credit, branch_id)
    WHERE is_posted;
