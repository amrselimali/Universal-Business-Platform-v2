-- 0026 — المرحلة 4B: المستندات التجارية الموحدة (فواتير/مرتجعات الشراء والمبيعات)
-- -----------------------------------------------------------------------------

CREATE TABLE commercial_documents (
    id                   uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    version              integer     NOT NULL DEFAULT 1,
    created_at           timestamptz NOT NULL DEFAULT now(),
    created_by           uuid REFERENCES users(id) ON DELETE SET NULL,
    updated_at           timestamptz,
    updated_by           uuid REFERENCES users(id) ON DELETE SET NULL,

    branch_id            uuid NOT NULL REFERENCES branches(id) ON DELETE RESTRICT,
    warehouse_id         uuid REFERENCES warehouses(id) ON DELETE RESTRICT,
    doc_no               varchar(30),
    doc_date             date NOT NULL,
    status               varchar(10) NOT NULL DEFAULT 'draft'
                         CHECK (status IN ('draft','posted','cancelled')),
    notes                varchar(500),
    posted_at            timestamptz,
    posted_by            uuid REFERENCES users(id) ON DELETE SET NULL,
    journal_entry_id     uuid REFERENCES journal_entries(id) ON DELETE RESTRICT,

    reversed_at          timestamptz,
    reversed_by          uuid REFERENCES users(id) ON DELETE SET NULL,
    reverse_reason       varchar(300),

    doc_type             varchar(20) NOT NULL CHECK (doc_type IN
                         ('purchase_invoice','purchase_return','sales_invoice','sales_return')),
    customer_id          uuid REFERENCES customers(id) ON DELETE RESTRICT,
    supplier_id          uuid REFERENCES suppliers(id) ON DELETE RESTRICT,
    party_name           varchar(255),
    payment_terms_id     uuid REFERENCES payment_terms(id) ON DELETE RESTRICT,
    due_date             date,
    price_list_id        uuid REFERENCES price_lists(id) ON DELETE RESTRICT,
    currency             char(3) NOT NULL DEFAULT 'EGP',

    subtotal             numeric(18,2) NOT NULL DEFAULT 0 CHECK (subtotal >= 0),
    discount_total       numeric(18,2) NOT NULL DEFAULT 0 CHECK (discount_total >= 0),
    tax_total            numeric(18,2) NOT NULL DEFAULT 0 CHECK (tax_total >= 0),
    total                numeric(18,2) NOT NULL DEFAULT 0 CHECK (total >= 0),
    allocated_total      numeric(18,2) NOT NULL DEFAULT 0 CHECK (allocated_total >= 0),

    stock_document_id    uuid REFERENCES stock_documents(id) ON DELETE RESTRICT,
    original_document_id uuid REFERENCES commercial_documents(id) ON DELETE RESTRICT,
    source_type          varchar(20) NOT NULL DEFAULT 'manual'
                         CHECK (source_type IN ('manual','booking','pos')),

    CONSTRAINT ck_doc_party CHECK (
        (doc_type IN ('sales_invoice','sales_return')
             AND customer_id IS NOT NULL AND supplier_id IS NULL)
     OR (doc_type IN ('purchase_invoice','purchase_return')
             AND supplier_id IS NOT NULL AND customer_id IS NULL)),
    CONSTRAINT ck_doc_return_source CHECK (
        (doc_type IN ('purchase_return','sales_return')) = (original_document_id IS NOT NULL))
);

COMMENT ON TABLE commercial_documents IS
    'مستند تجاري موحد: فاتورة/مرتجع شراء أو بيع — الترحيل ينشئ مستند مخزون داخلي وقيد أستاذ (N7)';

CREATE UNIQUE INDEX ux_commercial_documents_no
    ON commercial_documents (branch_id, doc_type, doc_no) WHERE doc_no IS NOT NULL;
CREATE UNIQUE INDEX ux_commercial_documents_original
    ON commercial_documents (original_document_id, doc_type) WHERE original_document_id IS NOT NULL;
CREATE INDEX ix_commercial_documents_branch_date
    ON commercial_documents (branch_id, doc_date DESC, id DESC);
CREATE INDEX ix_commercial_documents_supplier
    ON commercial_documents (supplier_id, doc_date DESC);
CREATE INDEX ix_commercial_documents_customer
    ON commercial_documents (customer_id, doc_date DESC);
CREATE INDEX ix_commercial_documents_unposted
    ON commercial_documents (doc_type, doc_date) WHERE status = 'posted' AND journal_entry_id IS NULL;

CREATE TRIGGER trg_touch_commercial_documents BEFORE UPDATE ON commercial_documents
FOR EACH ROW EXECUTE FUNCTION fn_touch_version();

CREATE OR REPLACE FUNCTION fn_commercial_doc_guard()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        IF OLD.status <> 'draft' THEN
            RAISE EXCEPTION 'POSTED_DOCUMENT_IMMUTABLE' USING ERRCODE = '23514';
        END IF;
        RETURN OLD;
    END IF;

    -- أي تعديل على مستند غير مسودة ممنوع (يشمل الملاحظات والمبالغ)
    IF OLD.status <> 'draft' THEN
        RAISE EXCEPTION 'POSTED_DOCUMENT_IMMUTABLE' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_commercial_doc_guard BEFORE UPDATE OR DELETE ON commercial_documents
FOR EACH ROW EXECUTE FUNCTION fn_commercial_doc_guard();

CREATE TABLE commercial_document_lines (
    id                uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    created_at        timestamptz NOT NULL DEFAULT now(),
    document_id       uuid NOT NULL REFERENCES commercial_documents(id) ON DELETE CASCADE,
    line_no           integer NOT NULL CHECK (line_no > 0),
    item_id           uuid NOT NULL REFERENCES items(id) ON DELETE RESTRICT,
    unit_id           uuid NOT NULL REFERENCES units(id) ON DELETE RESTRICT,
    conversion_factor numeric(18,6) NOT NULL DEFAULT 1 CHECK (conversion_factor > 0),
    quantity          numeric(18,4) NOT NULL CHECK (quantity > 0),
    unit_price        numeric(18,4) NOT NULL CHECK (unit_price >= 0),
    discount_percent  numeric(5,2) NOT NULL DEFAULT 0 CHECK (discount_percent BETWEEN 0 AND 100),
    discount_amount   numeric(18,2) NOT NULL DEFAULT 0 CHECK (discount_amount >= 0),
    net_amount        numeric(18,2) NOT NULL DEFAULT 0 CHECK (net_amount >= 0),
    tax_id            uuid REFERENCES taxes(id) ON DELETE RESTRICT,
    tax_rate          numeric(5,2) NOT NULL DEFAULT 0 CHECK (tax_rate BETWEEN 0 AND 100),
    tax_amount        numeric(18,2) NOT NULL DEFAULT 0 CHECK (tax_amount >= 0),
    total_amount      numeric(18,2) NOT NULL DEFAULT 0 CHECK (total_amount >= 0),
    unit_cost         numeric(18,6),
    cost_total        numeric(18,2),
    lot_number        varchar(60),
    expiry_date       date,
    returned_qty      numeric(18,4) NOT NULL DEFAULT 0 CHECK (returned_qty >= 0),
    description       varchar(300),
    CONSTRAINT ux_commercial_document_lines UNIQUE (document_id, line_no)
);

CREATE INDEX ix_commercial_lines_item ON commercial_document_lines (item_id, document_id);
CREATE INDEX ix_commercial_lines_doc ON commercial_document_lines (document_id);

CREATE OR REPLACE FUNCTION fn_commercial_line_guard()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_status varchar;
BEGIN
    IF TG_OP = 'DELETE' AND NOT EXISTS
        (SELECT 1 FROM commercial_documents WHERE id = OLD.document_id) THEN
        RETURN OLD;
    END IF;

    SELECT status INTO v_status FROM commercial_documents
    WHERE id = CASE WHEN TG_OP = 'DELETE' THEN OLD.document_id ELSE NEW.document_id END;

    IF v_status IS DISTINCT FROM 'draft' THEN
        RAISE EXCEPTION 'POSTED_DOCUMENT_IMMUTABLE' USING ERRCODE = '23514';
    END IF;
    RETURN CASE WHEN TG_OP = 'DELETE' THEN OLD ELSE NEW END;
END;
$$;

CREATE TRIGGER trg_commercial_line_guard BEFORE INSERT OR UPDATE OR DELETE
ON commercial_document_lines
FOR EACH ROW EXECUTE FUNCTION fn_commercial_line_guard();

-- حارس كمية المرتجع (N24)
CREATE TABLE commercial_return_links (
    id               uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    return_line_id   uuid NOT NULL UNIQUE REFERENCES commercial_document_lines(id) ON DELETE CASCADE,
    original_line_id uuid NOT NULL REFERENCES commercial_document_lines(id) ON DELETE RESTRICT,
    quantity         numeric(18,4) NOT NULL CHECK (quantity > 0),
    created_at       timestamptz NOT NULL DEFAULT now()
);

CREATE OR REPLACE FUNCTION fn_return_qty_guard()
RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE
    v_original_qty numeric;
    v_returned     numeric;
BEGIN
    SELECT quantity INTO v_original_qty FROM commercial_document_lines WHERE id = NEW.original_line_id;
    SELECT COALESCE(sum(quantity), 0) INTO v_returned
    FROM commercial_return_links
    WHERE original_line_id = NEW.original_line_id AND id <> NEW.id;

    IF v_returned + NEW.quantity > v_original_qty THEN
        RAISE EXCEPTION 'RETURN_EXCEEDS_ORIGINAL' USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_return_qty_guard BEFORE INSERT OR UPDATE ON commercial_return_links
FOR EACH ROW EXECUTE FUNCTION fn_return_qty_guard();

