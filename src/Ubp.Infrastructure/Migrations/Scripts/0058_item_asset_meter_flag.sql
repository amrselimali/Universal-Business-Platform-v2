-- 0058 — ربط الصنف بعدّاد أصل كخيار عام بدل جهاز محدد
ALTER TABLE items ADD COLUMN IF NOT EXISTS is_metered boolean NOT NULL DEFAULT false;

UPDATE items
   SET is_metered = true
 WHERE metered_asset_id IS NOT NULL
   AND NOT is_metered;

CREATE INDEX IF NOT EXISTS ix_items_is_metered ON items (is_metered)
    WHERE is_metered;
