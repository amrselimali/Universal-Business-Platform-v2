-- 0074 — الوردية على مستوى الفرع (كل الشاشات) + أرصدة افتتاحية محسوبة + المراجعة
-- ---------------------------------------------------------------------------
-- 1) الوردية لم تعد مرتبطة بخزينة واحدة: تفتح للفرع كله وتغطي كل الشاشات،
--    وواحدة فقط مفتوحة لكل فرع في نفس الوقت.
-- 2) رصيد الافتتاح يُحسب آلياً (مرحل من الحسابات + ورديات تحت المراجعة + عدادات
--    الأجهزة) ويُحفظ لقطة وقت الفتح — لا يُعدَّل يدوياً.
-- 3) عند الإغلاق تُحصر تسجيلات الوردية (مسودات ومرحّلة) في shift_review_items
--    لتُراجَع بواسطة يوزر محاسب، والوردية تُغلق آلياً 5 صباحاً.

ALTER TABLE cashier_shifts ALTER COLUMN treasury_id DROP NOT NULL;

DROP INDEX IF EXISTS ux_shift_open;
CREATE UNIQUE INDEX ux_shift_open ON cashier_shifts (branch_id) WHERE status = 'open';

ALTER TABLE cashier_shifts ADD COLUMN IF NOT EXISTS carried_balance numeric(18,2) NOT NULL DEFAULT 0;
ALTER TABLE cashier_shifts ADD COLUMN IF NOT EXISTS review_balance numeric(18,2) NOT NULL DEFAULT 0;
ALTER TABLE cashier_shifts ADD COLUMN IF NOT EXISTS meter_balance numeric(18,2) NOT NULL DEFAULT 0;
ALTER TABLE cashier_shifts ADD COLUMN IF NOT EXISTS auto_closed boolean NOT NULL DEFAULT false;
ALTER TABLE cashier_shifts ADD COLUMN IF NOT EXISTS review_status varchar(12) NOT NULL DEFAULT 'none';
ALTER TABLE cashier_shifts ADD COLUMN IF NOT EXISTS reviewed_at timestamptz;
ALTER TABLE cashier_shifts ADD COLUMN IF NOT EXISTS reviewed_by uuid REFERENCES users(id) ON DELETE SET NULL;

ALTER TABLE cashier_shifts DROP CONSTRAINT IF EXISTS ck_shift_review_status;
ALTER TABLE cashier_shifts ADD CONSTRAINT ck_shift_review_status
    CHECK (review_status IN ('none','pending','reviewed'));

COMMENT ON COLUMN cashier_shifts.treasury_id IS
    'اختياري/تاريخي: الوردية أصبحت على مستوى الفرع (كل الخزائن والشاشات) — 0074';
COMMENT ON COLUMN cashier_shifts.carried_balance IS
    'الرصيد المرحل من حسابات خزائن الفرع في الأستاذ العام وقت الفتح (محسوب)';
COMMENT ON COLUMN cashier_shifts.review_balance IS
    'قيمة المسودات المرحّلة للمراجعة من ورديات سابقة وقت الفتح (محسوبة)';
COMMENT ON COLUMN cashier_shifts.meter_balance IS
    'إجمالي قراءات عدادات الأجهزة للفرع وقت الفتح (محسوب)';
COMMENT ON COLUMN cashier_shifts.review_status IS
    'حالة مراجعة المحاسب لتسجيلات الوردية: none / pending / reviewed';

-- مستخدم مربوط بالوردية: لا يسجّل في أي شاشة إلا بوردية مفتوحة، وتسجيلاته مسودات
-- تُرحَّل للمراجعة عند الإغلاق. يُمنح هذا الوصف من شاشة تكويد المستخدم.
ALTER TABLE users ADD COLUMN IF NOT EXISTS requires_shift boolean NOT NULL DEFAULT false;
COMMENT ON COLUMN users.requires_shift IS
    'يمنع أي تسجيل في الشاشات بلا وردية مفتوحة، ولا يُرحّل للحسابات بنفسه (0074)';

CREATE TABLE IF NOT EXISTS shift_review_items (
    id           uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    shift_id     uuid NOT NULL REFERENCES cashier_shifts(id) ON DELETE CASCADE,
    screen_code  varchar(40)  NOT NULL,
    entity_type  varchar(40)  NOT NULL,
    entity_id    uuid         NOT NULL,
    doc_no       varchar(30),
    doc_date     date,
    party_name   varchar(255),
    amount       numeric(18,2) NOT NULL DEFAULT 0,
    doc_status   varchar(15)  NOT NULL DEFAULT 'draft',
    status       varchar(15)  NOT NULL DEFAULT 'pending'
                 CHECK (status IN ('pending','reviewed','rejected')),
    created_at   timestamptz  NOT NULL DEFAULT now(),
    reviewed_at  timestamptz,
    reviewed_by  uuid REFERENCES users(id) ON DELETE SET NULL,
    CONSTRAINT ux_shift_review_items UNIQUE (shift_id, entity_type, entity_id)
);

CREATE INDEX IF NOT EXISTS ix_shift_review_items_shift
    ON shift_review_items (shift_id, status, doc_date);

COMMENT ON TABLE shift_review_items IS
    'تسجيلات الوردية المحصورة عند الإغلاق: تُرحَّل للمراجعة بواسطة يوزر محاسب صاحب صلاحية الاعتماد.';
