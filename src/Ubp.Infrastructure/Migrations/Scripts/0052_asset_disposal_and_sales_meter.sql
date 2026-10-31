-- 0052 — استبعاد الأصول (ربح/خسارة) + ربط الصنف بعدّاد الأصل ومزامنة قراءات البيع
-- لا يُعدَّل أي ترحيل سابق (G1).

INSERT INTO posting_default_types (key, module, party_type, is_required, sort_order,
                                   label_ar, label_en)
SELECT 'asset_disposal_gain', 'assets', NULL, false, 80,
       'أرباح استبعاد أصول', 'Asset disposal gain'
WHERE NOT EXISTS (SELECT 1 FROM posting_default_types WHERE key = 'asset_disposal_gain');

INSERT INTO posting_default_types (key, module, party_type, is_required, sort_order,
                                   label_ar, label_en)
SELECT 'asset_disposal_loss', 'assets', NULL, false, 81,
       'خسائر استبعاد أصول', 'Asset disposal loss'
WHERE NOT EXISTS (SELECT 1 FROM posting_default_types WHERE key = 'asset_disposal_loss');

-- الصنف الذي يُستهلك على عدّاد أصل (خدمة طباعة/تصوير…)
ALTER TABLE items
    ADD COLUMN metered_asset_id uuid REFERENCES fixed_assets(id) ON DELETE SET NULL;

CREATE INDEX ix_items_metered_asset ON items (metered_asset_id)
    WHERE metered_asset_id IS NOT NULL;

-- قراءة عدّاد ناتجة عن سطر بيع: القيد الفريد يمنع احتساب نفس السطر مرتين
ALTER TABLE asset_meter_readings
    ADD COLUMN source_line_id uuid;

CREATE UNIQUE INDEX ux_asset_meter_readings_line
    ON asset_meter_readings (asset_id, source_line_id)
    WHERE source_line_id IS NOT NULL;

COMMENT ON COLUMN asset_meter_readings.source_line_id IS
    'سطر الفاتورة الذي أنتج قراءة SALES؛ يمنع المزامنة المتكررة.';
