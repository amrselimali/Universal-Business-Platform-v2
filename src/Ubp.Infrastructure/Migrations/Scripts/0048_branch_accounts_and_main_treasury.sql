-- 0048 — خزينة رئيسية واحدة + حسابات افتراضية لكل فرع (قرار أ+ج)
-- لا يُعدَّل أي ترحيل سابق (G1).

ALTER TABLE treasuries
    ADD COLUMN is_main boolean NOT NULL DEFAULT false;

-- خزينة رئيسية واحدة فقط على مستوى الشركة
CREATE UNIQUE INDEX ux_treasuries_single_main ON treasuries (is_main) WHERE is_main;

COMMENT ON COLUMN treasuries.is_main IS
    'الخزينة الرئيسية (واحدة فقط) التي تُجمَّع فيها أرصدة الفروع وتُستخدم للتحويلات بين الفروع.';

-- حسابات افتراضية لكل فرع: الشجرة تبقى عامة (D1) والفرع يحدد حساباته الافتراضية
CREATE TABLE branch_account_defaults (
    branch_id  uuid NOT NULL REFERENCES branches(id) ON DELETE CASCADE,
    key        varchar(60) NOT NULL,
    account_id uuid NOT NULL REFERENCES accounts(id) ON DELETE RESTRICT,
    updated_at timestamptz NOT NULL DEFAULT now(),
    updated_by uuid REFERENCES users(id) ON DELETE SET NULL,
    CONSTRAINT pk_branch_account_defaults PRIMARY KEY (branch_id, key),
    CONSTRAINT ck_branch_account_defaults_key CHECK (key ~ '^[a-z][a-z0-9_.]{2,59}$')
);

CREATE INDEX ix_branch_account_defaults_account ON branch_account_defaults (account_id);

COMMENT ON TABLE branch_account_defaults IS
    'حسابات افتراضية للفرع (خزينة/مخزون/إيراد/دفعات…): تُستخدم أولاً عند الترحيل ثم تسقط على posting_defaults العام.';
