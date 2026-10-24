-- 0027 — المرحلة 4C: الخزائن وطرق الدفع والورديات وسندات القبض/الصرف/التحويل
-- -----------------------------------------------------------------------------

CREATE TABLE treasuries (
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    is_active       boolean     NOT NULL DEFAULT true,
    version         integer     NOT NULL DEFAULT 1,
    created_at      timestamptz NOT NULL DEFAULT now(),
    created_by      uuid REFERENCES users(id) ON DELETE SET NULL,
    updated_at      timestamptz,
    updated_by      uuid REFERENCES users(id) ON DELETE SET NULL,
    branch_id       uuid NOT NULL REFERENCES branches(id) ON DELETE RESTRICT,
    code            varchar(30)  NOT NULL,
    name_ar         varchar(150) NOT NULL,
    name_en         varchar(150),
    treasury_type   varchar(20) NOT NULL CHECK (treasury_type IN ('cash','bank','wallet')),
    account_id      uuid NOT NULL REFERENCES accounts(id) ON DELETE RESTRICT,
    opening_balance numeric(18,2) NOT NULL DEFAULT 0,
    notes           varchar(300),
    CONSTRAINT ux_treasuries_code UNIQUE (branch_id, code)
);

COMMENT ON COLUMN treasuries.account_id IS 'حساب الخزينة في الأستاذ العام — D11';

CREATE INDEX ix_treasuries_branch ON treasuries (branch_id, is_active, code);

CREATE TRIGGER trg_touch_treasuries BEFORE UPDATE ON treasuries
FOR EACH ROW EXECUTE FUNCTION fn_touch_version();

CREATE TABLE treasury_methods (
    id                  uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    is_active           boolean     NOT NULL DEFAULT true,
    version             integer     NOT NULL DEFAULT 1,
    created_at          timestamptz NOT NULL DEFAULT now(),
    created_by          uuid REFERENCES users(id) ON DELETE SET NULL,
    updated_at          timestamptz,
    updated_by          uuid REFERENCES users(id) ON DELETE SET NULL,
    treasury_id         uuid NOT NULL REFERENCES treasuries(id) ON DELETE CASCADE,
    code                varchar(30)  NOT NULL,
    name_ar             varchar(150) NOT NULL,
    name_en             varchar(150),
    method_type         varchar(20) NOT NULL CHECK (method_type IN
                        ('cash','card','wallet','cheque','transfer')),
    clearing_account_id uuid REFERENCES accounts(id) ON DELETE RESTRICT,
    fee_percent         numeric(5,2)  NOT NULL DEFAULT 0 CHECK (fee_percent BETWEEN 0 AND 100),
    fee_fixed           numeric(18,2) NOT NULL DEFAULT 0 CHECK (fee_fixed >= 0),
    requires_reference  boolean NOT NULL DEFAULT false,
    CONSTRAINT ux_treasury_methods UNIQUE (treasury_id, code)
);

COMMENT ON COLUMN treasury_methods.clearing_account_id IS
    'حساب وسيط للبطاقات/المحافظ/الشيكات يُسوّى لاحقاً — D12';

CREATE TRIGGER trg_touch_treasury_methods BEFORE UPDATE ON treasury_methods
FOR EACH ROW EXECUTE FUNCTION fn_touch_version();

CREATE TABLE vouchers (
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    version         integer     NOT NULL DEFAULT 1,
    created_at      timestamptz NOT NULL DEFAULT now(),
    created_by      uuid REFERENCES users(id) ON DELETE SET NULL,
    updated_at      timestamptz,
    updated_by      uuid REFERENCES users(id) ON DELETE SET NULL,
    branch_id       uuid NOT NULL REFERENCES branches(id) ON DELETE RESTRICT,
    doc_no          varchar(30),
    doc_date        date NOT NULL,
    status          varchar(10) NOT NULL DEFAULT 'draft'
                    CHECK (status IN ('draft','posted','cancelled')),
    notes           varchar(500),
    posted_at       timestamptz,
    posted_by       uuid REFERENCES users(id) ON DELETE SET NULL,
    journal_entry_id uuid REFERENCES journal_entries(id) ON DELETE RESTRICT,
    voucher_type    varchar(10) NOT NULL CHECK (voucher_type IN ('receipt','payment','transfer')),
    party_type      char(1) CHECK (party_type IN ('C','S')),
    party_id        uuid,
    party_name      varchar(255),
    treasury_id     uuid NOT NULL REFERENCES treasuries(id) ON DELETE RESTRICT,
    to_treasury_id  uuid REFERENCES treasuries(id) ON DELETE RESTRICT,
    total_amount    numeric(18,2) NOT NULL DEFAULT 0 CHECK (total_amount >= 0),
    fee_total       numeric(18,2) NOT NULL DEFAULT 0 CHECK (fee_total >= 0),
    CONSTRAINT ck_voucher_party CHECK (voucher_type = 'transfer'
        OR (party_type IS NOT NULL AND party_id IS NOT NULL)),
    CONSTRAINT ck_voucher_transfer CHECK (voucher_type <> 'transfer'
        OR (to_treasury_id IS NOT NULL AND to_treasury_id <> treasury_id))
);

CREATE UNIQUE INDEX ux_vouchers_no ON vouchers (branch_id, voucher_type, doc_no)
    WHERE doc_no IS NOT NULL;
CREATE INDEX ix_vouchers_date ON vouchers (branch_id, doc_date DESC, id DESC);
CREATE INDEX ix_vouchers_party ON vouchers (party_type, party_id, doc_date DESC);

CREATE TRIGGER trg_touch_vouchers BEFORE UPDATE ON vouchers
FOR EACH ROW EXECUTE FUNCTION fn_touch_version();

CREATE OR REPLACE FUNCTION fn_voucher_guard()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        IF OLD.status <> 'draft' THEN
            RAISE EXCEPTION 'POSTED_VOUCHER_IMMUTABLE' USING ERRCODE = '23514';
        END IF;
        RETURN OLD;
    END IF;
    IF OLD.status <> 'draft' THEN
        RAISE EXCEPTION 'POSTED_VOUCHER_IMMUTABLE' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_voucher_guard BEFORE UPDATE OR DELETE ON vouchers
FOR EACH ROW EXECUTE FUNCTION fn_voucher_guard();

CREATE TABLE voucher_lines (
    id                 uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    created_at         timestamptz NOT NULL DEFAULT now(),
    voucher_id         uuid NOT NULL REFERENCES vouchers(id) ON DELETE CASCADE,
    line_no            integer NOT NULL CHECK (line_no > 0),
    treasury_method_id uuid NOT NULL REFERENCES treasury_methods(id) ON DELETE RESTRICT,
    amount             numeric(18,2) NOT NULL CHECK (amount > 0),
    fee_amount         numeric(18,2) NOT NULL DEFAULT 0 CHECK (fee_amount >= 0),
    reference          varchar(100),
    UNIQUE (voucher_id, line_no)
);

CREATE OR REPLACE FUNCTION fn_voucher_line_guard()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE v_status varchar;
BEGIN
    IF TG_OP = 'DELETE' AND NOT EXISTS (SELECT 1 FROM vouchers WHERE id = OLD.voucher_id) THEN
        RETURN OLD;
    END IF;
    SELECT status INTO v_status FROM vouchers
    WHERE id = CASE WHEN TG_OP = 'DELETE' THEN OLD.voucher_id ELSE NEW.voucher_id END;
    IF v_status IS DISTINCT FROM 'draft' THEN
        RAISE EXCEPTION 'POSTED_VOUCHER_IMMUTABLE' USING ERRCODE = '23514';
    END IF;
    RETURN CASE WHEN TG_OP = 'DELETE' THEN OLD ELSE NEW END;
END;
$$;

CREATE TRIGGER trg_voucher_line_guard BEFORE INSERT OR UPDATE OR DELETE ON voucher_lines
FOR EACH ROW EXECUTE FUNCTION fn_voucher_line_guard();

-- تخصيص السندات على الفواتير (N12)
CREATE TABLE document_allocations (
    id                 uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    created_at         timestamptz NOT NULL DEFAULT now(),
    created_by         uuid REFERENCES users(id) ON DELETE SET NULL,
    source_voucher_id  uuid NOT NULL REFERENCES vouchers(id) ON DELETE CASCADE,
    target_document_id uuid NOT NULL REFERENCES commercial_documents(id) ON DELETE RESTRICT,
    amount             numeric(18,2) NOT NULL CHECK (amount > 0)
);

CREATE UNIQUE INDEX ux_allocations ON document_allocations (source_voucher_id, target_document_id);


CREATE TABLE cashier_shifts (
    id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    is_active     boolean     NOT NULL DEFAULT true,
    version       integer     NOT NULL DEFAULT 1,
    created_at    timestamptz NOT NULL DEFAULT now(),
    created_by    uuid REFERENCES users(id) ON DELETE SET NULL,
    updated_at    timestamptz,
    updated_by    uuid REFERENCES users(id) ON DELETE SET NULL,
    branch_id     uuid NOT NULL REFERENCES branches(id) ON DELETE RESTRICT,
    treasury_id   uuid NOT NULL REFERENCES treasuries(id) ON DELETE RESTRICT,
    user_id       uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    code          varchar(30) NOT NULL,
    opened_at     timestamptz NOT NULL DEFAULT now(),
    closed_at     timestamptz,
    opening_cash  numeric(18,2) NOT NULL DEFAULT 0,
    expected_cash numeric(18,2) NOT NULL DEFAULT 0,
    counted_cash  numeric(18,2),
    variance      numeric(18,2),
    status        varchar(10) NOT NULL DEFAULT 'open'
                  CHECK (status IN ('open','closed','reopened')),
    closed_by     uuid REFERENCES users(id) ON DELETE SET NULL,
    reopened_by   uuid REFERENCES users(id) ON DELETE SET NULL,
    notes         varchar(500),
    CONSTRAINT ux_cashier_shifts UNIQUE (branch_id, code)
);

CREATE UNIQUE INDEX ux_shift_open ON cashier_shifts (treasury_id) WHERE status = 'open';
CREATE INDEX ix_shifts_branch ON cashier_shifts (branch_id, opened_at DESC);

CREATE TRIGGER trg_touch_cashier_shifts BEFORE UPDATE ON cashier_shifts
FOR EACH ROW EXECUTE FUNCTION fn_touch_version();

CREATE TABLE shift_denominations (
    id           uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    shift_id     uuid NOT NULL REFERENCES cashier_shifts(id) ON DELETE CASCADE,
    denomination numeric(18,2) NOT NULL CHECK (denomination > 0),
    count        integer NOT NULL CHECK (count >= 0),
    UNIQUE (shift_id, denomination)
);

-- مفاتيح الترحيل الخاصة بالخزينة (G1: تُضاف في مرحلة مستهلكها)
INSERT INTO posting_default_types (key, module, party_type, is_required, sort_order, label_ar, label_en)
VALUES
    ('customer_advances', 'treasury', 'C', true, 410, 'دفعات مقدمة من العملاء', 'Customer advances'),
    ('supplier_advances', 'treasury', 'S', true, 420, 'دفعات مقدمة للموردين', 'Supplier advances'),
    ('payment_fees', 'treasury', NULL, false, 430, 'مصاريف التحصيل والدفع', 'Payment fees'),
    ('cash_over_short', 'treasury', NULL, false, 440, 'زيادة/عجز الخزينة', 'Cash over/short'),
    ('inter_branch_clearing', 'treasury', NULL, false, 450, 'تسوية بين الفروع', 'Inter-branch clearing')
ON CONFLICT (key) DO NOTHING;

