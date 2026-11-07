-- 0092 — فلتر فروع الجلسة: المستخدم المربوط بأكثر من فرع يختار كل/بعض الفروع
ALTER TABLE user_sessions ADD COLUMN IF NOT EXISTS filter_branch_ids uuid[];
COMMENT ON COLUMN user_sessions.filter_branch_ids IS
    'الفروع المختارة في الجلسة (NULL = كل فروع المستخدم) — 0092.';