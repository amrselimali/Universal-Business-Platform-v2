-- 0087 — ربط الشكوى بالخدمة/الصنف لإظهار «متسبب الشكوى» في تقرير الوردية بجانب منفّذ الخدمة
ALTER TABLE complaints ADD COLUMN IF NOT EXISTS item_id uuid REFERENCES items(id) ON DELETE SET NULL;
CREATE INDEX IF NOT EXISTS ix_complaints_item ON complaints (item_id);
COMMENT ON COLUMN complaints.item_id IS
    'الخدمة/الصنف محل الشكوى (اختياري) — يُستخدم لمطابقة متسبب الشكوى في تقرير الوردية.';
