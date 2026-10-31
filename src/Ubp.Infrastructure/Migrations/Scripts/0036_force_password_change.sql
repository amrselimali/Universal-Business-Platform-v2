-- 0036 — إجبار تغيير كلمة المرور عند أول دخول
-- لا يُعدَّل أي ترحيل منفَّذ (G1)؛ هذا سكريبت جديد ومتسلسل بعد 0035.

ALTER TABLE users
    ADD COLUMN must_change_password boolean NOT NULL DEFAULT false,
    ADD COLUMN password_changed_at   timestamptz;

-- الحسابات التي ما زالت تحمل كلمة المرور الافتراضية المزروعة في 0009 تُطالب بالتغيير فوراً.
UPDATE users
SET must_change_password = true
WHERE is_super_admin
  AND password_hash = crypt('Admin@123', password_hash);

COMMENT ON COLUMN users.must_change_password IS
    'عند true يُمنع الوصول لكل الشاشات وواجهات API باستثناء تغيير كلمة المرور.';
COMMENT ON COLUMN users.password_changed_at IS
    'وقت آخر تغيير ناجح لكلمة المرور؛ يُضبط من SetPasswordAsync.';
