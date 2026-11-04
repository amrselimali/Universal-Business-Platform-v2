-- 0073 — تسويات عدّاد الأصول: حركات تصحيحية (+/-) وحدات مع ملاحظة، مستقلة عن القراءات التصاعدية.
-- تُعرض في «متابعة العداد» (إجماليات شهرية/يومية) وتُضاف إلى رصيد العدّاد في نفس الفترة.

CREATE TABLE IF NOT EXISTS asset_meter_adjustments (
    id          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    asset_id    uuid          NOT NULL REFERENCES fixed_assets(id) ON DELETE CASCADE,
    adjusted_at timestamptz   NOT NULL DEFAULT now(),
    units       numeric(18,2) NOT NULL CHECK (units <> 0),
    note        varchar(300),
    created_by  uuid REFERENCES users(id) ON DELETE SET NULL,
    created_at  timestamptz   NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_asset_meter_adjustments_asset
    ON asset_meter_adjustments (asset_id, adjusted_at DESC);

COMMENT ON TABLE asset_meter_adjustments IS
    'تسويات عدّاد الأصل: وحدات موجبة/سالبة مع ملاحظة تُضاف لرصيد العدّاد في تقرير متابعة العداد.';
