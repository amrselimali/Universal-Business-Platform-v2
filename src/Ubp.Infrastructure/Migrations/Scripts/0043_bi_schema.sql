-- 0043 — مخطط التحليلات bi: عروض مادية للقراءة فقط + دور bi_reader
-- لا يُعدَّل أي ترحيل سابق (G1).

CREATE SCHEMA IF NOT EXISTS bi;

-- بعض القواعد المُدارة (مثل Neon) تُقيّد إنشاء الأدوار؛ لا نُفشل الترحيل بسبب ذلك.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'bi_reader') THEN
        BEGIN
            CREATE ROLE bi_reader NOLOGIN;
        EXCEPTION WHEN insufficient_privilege THEN
            RAISE NOTICE 'bi_reader was not created (insufficient privilege); grants skipped';
        END;
    END IF;
END $$;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'bi_reader') THEN
        GRANT USAGE ON SCHEMA bi TO bi_reader;
    END IF;
END $$;

-- مبيعات يومية (نسخة تحليلية من جدول الإجماليات)
CREATE MATERIALIZED VIEW bi.sales_daily AS
SELECT day, branch_id, invoice_count, return_count,
       net_total, tax_total, gross_total, cogs_total, updated_at
FROM sales_daily_totals
WITH DATA;

-- فريد لأن REFRESH CONCURRENTLY يشترطه
CREATE UNIQUE INDEX ux_bi_sales_daily ON bi.sales_daily (day, branch_id);

-- قيمة المخزون لكل مخزن
CREATE MATERIALIZED VIEW bi.stock_value_by_warehouse AS
SELECT b.warehouse_id,
       w.code   AS warehouse_code,
       w.name_ar AS warehouse_name,
       SUM(b.on_hand)         AS on_hand,
       SUM(b.inventory_value) AS inventory_value
FROM stock_balances b
JOIN warehouses w ON w.id = b.warehouse_id
GROUP BY b.warehouse_id, w.code, w.name_ar
WITH DATA;

CREATE UNIQUE INDEX ux_bi_stock_value ON bi.stock_value_by_warehouse (warehouse_id);

-- أرصدة الأطراف الشهرية
CREATE MATERIALIZED VIEW bi.party_balances_monthly AS
SELECT period_month, party_type, party_id,
       SUM(debit)            AS debit_total,
       SUM(credit)           AS credit_total,
       SUM(debit - credit)   AS balance
FROM party_balances
GROUP BY period_month, party_type, party_id
WITH DATA;

CREATE UNIQUE INDEX ux_bi_party_balances
    ON bi.party_balances_monthly (period_month, party_type, party_id);

-- تحديث غير متزامن بسيط (CONCURRENTLY يُنفَّذ من التطبيق كجملة مستقلة)
CREATE OR REPLACE FUNCTION bi.fn_refresh_all() RETURNS void AS $$
BEGIN
    REFRESH MATERIALIZED VIEW bi.sales_daily;
    REFRESH MATERIALIZED VIEW bi.stock_value_by_warehouse;
    REFRESH MATERIALIZED VIEW bi.party_balances_monthly;
END $$ LANGUAGE plpgsql;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'bi_reader') THEN
        GRANT SELECT ON ALL TABLES IN SCHEMA bi TO bi_reader;
        ALTER DEFAULT PRIVILEGES IN SCHEMA bi GRANT SELECT ON TABLES TO bi_reader;
    END IF;
END $$;

COMMENT ON SCHEMA bi IS 'مخطط تحليلات للقراءة فقط؛ المصدر هو الجداول التشغيلية ولا يُكتب فيه.';

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'bi_reader') THEN
        COMMENT ON ROLE bi_reader IS
            'دور قراءة فقط لمخطط bi (لأدوات التحليل)؛ لا يملك صلاحية على public.';
    END IF;
END $$;
