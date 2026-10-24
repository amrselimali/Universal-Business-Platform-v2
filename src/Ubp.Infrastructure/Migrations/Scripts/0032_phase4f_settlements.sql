-- 0032 — المرحلة 4F: التسويات والعمولات والأعمار ومركز الترحيل
-- -----------------------------------------------------------------------------

ALTER TABLE app_settings ADD COLUMN IF NOT EXISTS commission_basis smallint NOT NULL DEFAULT 1
    CHECK (commission_basis BETWEEN 1 AND 3);

CREATE TABLE booking_settlements (
    id                uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    version           integer     NOT NULL DEFAULT 1,
    created_at        timestamptz NOT NULL DEFAULT now(),
    created_by        uuid REFERENCES users(id) ON DELETE SET NULL,
    updated_at        timestamptz,
    updated_by        uuid REFERENCES users(id) ON DELETE SET NULL,
    branch_id         uuid NOT NULL REFERENCES branches(id) ON DELETE RESTRICT,
    doc_no            varchar(30),
    doc_date          date NOT NULL,
    status            varchar(10) NOT NULL DEFAULT 'draft'
                      CHECK (status IN ('draft','posted','cancelled')),
    booking_id        uuid NOT NULL REFERENCES bookings(id) ON DELETE RESTRICT,
    settlement_type   varchar(25) NOT NULL CHECK (settlement_type IN
                      ('refund','cancel_qty','refund_and_cancel','transfer','forfeit')),
    amount            numeric(18,2) NOT NULL DEFAULT 0 CHECK (amount >= 0),
    treasury_id       uuid REFERENCES treasuries(id) ON DELETE RESTRICT,
    refund_voucher_id uuid REFERENCES vouchers(id) ON DELETE RESTRICT,
    target_booking_id uuid REFERENCES bookings(id) ON DELETE RESTRICT,
    reason            varchar(300),
    approved_by       uuid REFERENCES users(id) ON DELETE SET NULL,
    approved_at       timestamptz,
    journal_entry_id  uuid REFERENCES journal_entries(id) ON DELETE RESTRICT,
    posted_at         timestamptz,
    posted_by         uuid REFERENCES users(id) ON DELETE SET NULL,
    notes             varchar(500)
);

CREATE UNIQUE INDEX ux_settlement_no ON booking_settlements (branch_id, doc_no)
    WHERE doc_no IS NOT NULL;
CREATE INDEX ix_settlements_booking ON booking_settlements (booking_id, doc_date DESC);

CREATE TRIGGER trg_touch_settlements BEFORE UPDATE ON booking_settlements
FOR EACH ROW EXECUTE FUNCTION fn_touch_version();

CREATE TABLE employee_commissions (
    id                uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    employee_id       uuid NOT NULL REFERENCES employees(id) ON DELETE RESTRICT,
    document_id       uuid NOT NULL REFERENCES commercial_documents(id) ON DELETE CASCADE,
    document_line_id  uuid REFERENCES commercial_document_lines(id) ON DELETE CASCADE,
    basis             smallint NOT NULL CHECK (basis BETWEEN 1 AND 3),
    base_amount       numeric(18,2) NOT NULL,
    rate              numeric(5,2) NOT NULL CHECK (rate BETWEEN 0 AND 100),
    amount            numeric(18,2) NOT NULL,
    period_month      date NOT NULL,
    created_at        timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ux_commissions UNIQUE (employee_id, document_line_id, period_month)
);

CREATE INDEX ix_commissions_period ON employee_commissions (period_month, employee_id);

INSERT INTO posting_default_types (key, module, party_type, is_required, sort_order, label_ar, label_en)
VALUES ('booking_forfeit_income', 'bookings', NULL, false, 460,
        'إيراد مصادرة الحجوزات', 'Booking forfeit income')
ON CONFLICT (key) DO NOTHING;

-- المستندات المرحّلة بلا قيد (D19) — تُستخدم في مركز الترحيل
CREATE OR REPLACE VIEW v_unposted_documents AS
    SELECT 'commercial_document'::varchar AS kind, d.id, d.branch_id, d.doc_type AS doc_type,
           d.doc_no, d.doc_date, d.status, d.total AS amount
    FROM commercial_documents d
    WHERE d.status = 'posted' AND d.journal_entry_id IS NULL
    UNION ALL
    SELECT 'voucher'::varchar, v.id, v.branch_id, v.voucher_type, v.doc_no, v.doc_date,
           v.status, v.total_amount
    FROM vouchers v
    WHERE v.status = 'posted' AND v.journal_entry_id IS NULL
    UNION ALL
    SELECT 'delivery_note'::varchar, n.id, n.branch_id, 'delivery_note', n.doc_no, n.doc_date,
           n.status, 0
    FROM delivery_notes n
    WHERE n.status = 'posted' AND n.journal_entry_id IS NULL;
