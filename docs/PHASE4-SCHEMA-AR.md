# كتالوج المخطط — المرحلة 4/6/7

> كل جدول جديد يتبع المعايير أدناه. أسماء الأعمدة هنا **نهائية**؛ أي اختلاف مع migration يُعد خطأً.
> المصدر المرجعي للجداول القائمة: `0001`–`0023`.

---

## 1) المعايير

```
STD     id uuid PK · is_active bool NN d=true · version int NN d=1
        created_at timestamptz NN d=now() · created_by uuid FK users NULL
        updated_at timestamptz NULL · updated_by uuid FK users NULL

STD-DOC كل STD عدا is_active (الحالة تحل محلها)

DOC     STD-DOC + branch_id uuid NN FK branches · doc_no varchar(30) NULL
        doc_date date NN · status varchar(10) NN d='draft' CHECK (draft|posted|cancelled)
        notes varchar(500) NULL · posted_at timestamptz NULL · posted_by uuid FK users NULL
        journal_entry_id uuid FK journal_entries NULL

REV     reversed_at timestamptz NULL · reversed_by uuid FK users NULL
        reverse_reason varchar(300) NULL

LINE    id uuid PK · created_at timestamptz NN d=now()
        document_id uuid NN FK رأس ON DELETE CASCADE · line_no int NN
        UNIQUE (document_id, line_no)
```

| النوع | التعريف |
|---|---|
| مبلغ | `numeric(18,2)` |
| كمية | `numeric(18,4)` |
| سعر | `numeric(18,4)` |
| تكلفة وحدة | `numeric(18,6)` |
| نسبة | `numeric(5,2)` CHECK BETWEEN 0 AND 100 |

**قواعد تسمية:** جمع snake_case · `_id` للمفاتيح · `_at` للطوابع · `is_` للمنطقي · `_total` للمجاميع · `_date` للتواريخ.

**سياسة الفهارس:** فهرس على كل FK مُفلتر · `(branch_id, doc_date DESC, id DESC)` لكل مستند · فهارس جزئية `WHERE status <> 'cancelled'` · GIN trigram لكل نص مبحوث عبر `normalize_search`.

---

## 2) 4A-1 — `0024_phase4a1_core.sql`

### 2.1 توسيع `app_settings` (أعمدة مُصنّفة — لا Key/Value)
```sql
booking_default_due_days     smallint NN d=30 CHECK BETWEEN 0 AND 365
block_expired_issue          boolean  NN d=true
delivery_requires_collection smallint NN d=0  CHECK BETWEEN 0 AND 2 -- 0 لا / 1 اختياري / 2 إجباري
reserve_stock_on_booking     boolean  NN d=true
auto_invoice_on_delivery     boolean  NN d=true
invoice_grouping_mode        smallint NN d=0  CHECK BETWEEN 0 AND 2 -- 0 فوري / 1 عند الإغلاق / 2 يدوي
appointment_slot_minutes     smallint NN d=15 CHECK BETWEEN 5 AND 240
pos_receipt_footer           varchar(300) NULL
auto_follow_up_overdue       boolean  NN d=false
```
> `commission_basis` **لا يُضاف هنا** — يُضاف في `0030` مع مستهلكه (قاعدة منع الإعداد الميت).

### 2.2 توسيع `posting_defaults` + الجدول المرجعي
```sql
ALTER TABLE posting_defaults ADD COLUMN party_type char(1) NULL CHECK (party_type IN ('C','S'));

CREATE TABLE posting_default_types (
    key          varchar(50) PRIMARY KEY,
    module       varchar(30) NN,                        -- inventory | sales | purchasing | treasury | bookings
    party_type   char(1) NULL CHECK (party_type IN ('C','S')),
    is_required  boolean NN d=true,
    sort_order   smallint NN d=0,
    label_ar     varchar(150) NN,
    label_en     varchar(150) NULL,
    is_active    boolean NN d=true
);
```
**Seed (0024):** المفاتيح الستة القائمة (`stock_inventory, stock_cogs, stock_adjustment_gain, stock_adjustment_loss, opening_equity, rounding`) بـ`module='inventory'`، ثم مفاتيح الفواتير:
`sales_revenue` (sales) · `vat_output` (sales) · `vat_input` (purchasing) · `customer_receivable` C (sales) · `supplier_payable` S (purchasing) · `purchase_variance` (purchasing) · `rounding_difference` (shared).

> مفاتيح الخزينة تُضاف في `0027` والمصادرة/العمولات في `0030` — كل مرحلة تضيف مفاتيحها (G1).

### 2.3 الموظفون
```sql
CREATE TABLE employees (
    id uuid PK, is_active bool NN d=true, version int NN d=1,
    created_at timestamptz NN d=now(), created_by uuid FK users NULL,
    updated_at timestamptz NULL, updated_by uuid FK users NULL,
    code varchar(30) NN UNIQUE, name_ar varchar(200) NN, name_en varchar(200) NULL,
    phone varchar(30) NULL, email varchar(200) NULL,
    user_id uuid NULL FK users ON DELETE SET NULL,
    is_booking_receiver boolean NN d=false,   -- D8
    is_seller           boolean NN d=false,
    is_service_executor boolean NN d=false,
    hire_date date NULL, notes text NULL,
    search_text text GENERATED (code||name_ar||name_en||phone) STORED);
```
فهارس: GIN trigram على `search_text` · `(is_active, code)` · `(user_id)`.

### 2.4 الضرائب وشروط الدفع وقوائم الأسعار
```sql
CREATE TABLE taxes (id, is_active, version, created_at/by, updated_at/by,
    code varchar(20) UNIQUE, name_ar varchar(100) NN, name_en varchar(100) NULL,
    rate numeric(5,2) NN CHECK BETWEEN 0 AND 100,
    is_inclusive_allowed boolean NN d=true,
    output_account_id uuid NULL FK accounts, input_account_id uuid NULL FK accounts,
    notes text);

CREATE TABLE payment_terms (id, is_active, version, created_at/by, updated_at/by,
    code varchar(20) UNIQUE, name_ar NN, name_en NULL,
    net_days smallint NN d=0 CHECK BETWEEN 0 AND 365,
    discount_percent numeric(5,2) NN d=0, discount_days smallint NN d=0);

CREATE TABLE price_lists (id, is_active, version, created_at/by, updated_at/by,
    code varchar(20) UNIQUE, name_ar NN, name_en NULL,
    currency char(3) NN d='EGP', is_default boolean NN d=false,
    valid_from date NULL, valid_to date NULL);
CREATE UNIQUE INDEX ux_price_lists_default ON price_lists (is_default) WHERE is_default;

CREATE TABLE price_list_items (
    id uuid PK, price_list_id uuid NN FK price_lists ON DELETE CASCADE,
    item_id uuid NN FK items, unit_id uuid NULL FK units,
    price numeric(18,4) NN CHECK >= 0, min_qty numeric(18,4) NN d=0,
    created_at timestamptz NN d=now(),
    UNIQUE (price_list_id, item_id, unit_id, min_qty));
```


### 2.5 وصفات الخدمات (D14/D15)
```sql
CREATE TABLE service_recipes (id uuid PK, is_active, version, created_at/by, updated_at/by,
    item_id uuid NN UNIQUE FK items, notes varchar(500));

CREATE TABLE service_recipe_items (
    id uuid PK, recipe_id uuid NN FK service_recipes ON DELETE CASCADE,
    line_no int NN, component_item_id uuid NN FK items, unit_id uuid NN FK units,
    quantity numeric(18,4) NN CHECK > 0,
    consumption_mode smallint NN d=0 CHECK BETWEEN 0 AND 2, -- 0 ثابت / 1 قابل للتعديل / 2 يدوي
    is_required boolean NN d=true, notes varchar(300),
    UNIQUE (recipe_id, line_no));

CREATE TABLE recipe_substitutes (
    id uuid PK, recipe_item_id uuid NN FK service_recipe_items ON DELETE CASCADE,
    substitute_item_id uuid NN FK items, priority smallint NN d=0,
    UNIQUE (recipe_item_id, substitute_item_id));
```

### 2.6 تعديلات الأصناف والعملاء والمخازن
```sql
ALTER TABLE items ADD COLUMN tax_id uuid NULL FK taxes;
ALTER TABLE items ADD COLUMN price_includes_tax boolean NN d=false;
ALTER TABLE items ADD COLUMN track_expiry boolean NN d=false;
ALTER TABLE items ADD COLUMN is_consumable boolean NN d=false;
ALTER TABLE items ADD COLUMN default_price numeric(18,4) NULL CHECK >= 0;

CREATE TABLE customer_branch_limits (
    id uuid PK, customer_id uuid NN FK customers ON DELETE CASCADE,
    branch_id uuid NN FK branches, credit_limit numeric(18,2) NN d=0,
    sales_limit numeric(18,2) NN d=0, is_active boolean NN d=true,
    created_at timestamptz NN d=now(), UNIQUE (customer_id, branch_id));

ALTER TABLE warehouses ADD COLUMN warehouse_type varchar(20) NN d='MAIN'
    CHECK (warehouse_type IN ('MAIN','CONSUMABLES'));

CREATE TABLE branch_settings (          -- N10: مخزن المستهلكات لكل فرع
    branch_id uuid PRIMARY KEY FK branches ON DELETE CASCADE,
    consumables_warehouse_id uuid NULL FK warehouses,
    updated_at timestamptz NULL, updated_by uuid NULL FK users);
-- Trigger: المخزن المحدد يجب أن يكون warehouse_type='CONSUMABLES' وتابعاً لنفس الفرع
```

---

## 3) 4A-2 — `0025_phase4a2_lots_fefo.sql`

```sql
CREATE TABLE stock_lots (
    id uuid PK,
    item_id uuid NN FK items, warehouse_id uuid NN FK warehouses,
    lot_number varchar(60) NN,
    manufacture_date date NULL, expiry_date date NULL,
    quantity_on_hand numeric(18,4) NN d=0 CHECK >= 0,
    quantity_reserved numeric(18,4) NN d=0 CHECK >= 0,
    unit_cost numeric(18,6) NN d=0 CHECK >= 0,      -- N8: تكلفة الدفعة
    received_at timestamptz NN d=now(),
    source_document_id uuid NULL FK stock_documents,
    is_active boolean NN d=true, version int NN d=1,
    created_at/by, updated_at/by,
    UNIQUE (item_id, warehouse_id, lot_number),
    CONSTRAINT ck_lots_reserved CHECK (quantity_reserved <= quantity_on_hand));

CREATE INDEX ix_lots_fefo ON stock_lots (item_id, warehouse_id, expiry_date NULLS LAST, id)
    WHERE quantity_on_hand > 0;
CREATE INDEX ix_lots_expiry ON stock_lots (expiry_date)
    WHERE quantity_on_hand > 0 AND expiry_date IS NOT NULL;

CREATE TABLE stock_lot_movements (
    id bigint GENERATED ALWAYS AS IDENTITY PK,
    lot_id uuid NN FK stock_lots, document_id uuid NN FK stock_documents,
    document_line_id uuid NULL FK stock_document_lines,
    direction char(1) NN CHECK IN ('I','O'),
    quantity numeric(18,4) NN CHECK > 0, unit_cost numeric(18,6) NN d=0,
    created_at timestamptz NN d=now(), created_by uuid NULL FK users);
CREATE INDEX ix_lot_moves_lot ON stock_lot_movements (lot_id, created_at DESC);

ALTER TABLE stock_balances ADD COLUMN reserved_qty numeric(18,4) NN d=0 CHECK >= 0; -- N11 كاش

-- إضافة نوع DISPOSAL: القيد الحالي مولّد تلقائياً باسم stock_documents_doc_type_check
-- (يُتحقق منه بـ \d stock_documents قبل التنفيذ)
ALTER TABLE stock_documents DROP CONSTRAINT stock_documents_doc_type_check;
ALTER TABLE stock_documents ADD CONSTRAINT ck_stock_documents_type
    CHECK (doc_type IN ('RECEIPT','ISSUE','TRANSFER','ADJUSTMENT','OPENING','REVERSAL','DISPOSAL'));
```
**Backfill (نفس السكريبت، بعد إنشاء الجداول):**
```sql
INSERT INTO stock_lots (id, item_id, warehouse_id, lot_number, expiry_date,
                        quantity_on_hand, unit_cost, received_at)
SELECT gen_random_uuid(), b.item_id, b.warehouse_id, 'OPENING', NULL,
       b.on_hand, b.average_cost, now()
FROM stock_balances b WHERE b.on_hand > 0;
-- تحقق: SELECT count(*) FROM stock_balances b
--        WHERE b.on_hand <> (SELECT coalesce(sum(l.quantity_on_hand),0) FROM stock_lots l
--                            WHERE l.item_id=b.item_id AND l.warehouse_id=b.warehouse_id);
```
**تعديل المحرك:** `fn_stock_apply` يُعاد تعريفه (CREATE OR REPLACE في 0025) ليخصم من الدفعات بترتيب FEFO للأصناف `track_expiry=true`، ويحدّث `quantity_on_hand`/`quantity_reserved` و`stock_balances` في نفس المعاملة، ويرفض الرصيد السالب للمتتبَّع.


---

## 4) 4B — `0026_phase4b_purchasing.sql` (الجدول الموحد)

```sql
CREATE TABLE commercial_documents (
    id uuid PK, version int NN d=1, created_at/by, updated_at/by,
    branch_id uuid NN FK branches, doc_no varchar(30) NULL, doc_date date NN,
    status varchar(10) NN d='draft' CHECK (status IN ('draft','posted','cancelled')),
    notes varchar(500) NULL, posted_at timestamptz NULL, posted_by uuid NULL FK users,
    journal_entry_id uuid NULL FK journal_entries,
    reversed_at timestamptz NULL, reversed_by uuid NULL FK users, reverse_reason varchar(300),

    doc_type varchar(20) NN CHECK (doc_type IN
        ('purchase_invoice','purchase_return','sales_invoice','sales_return')),
    customer_id uuid NULL FK customers,      -- N6: عمودان + CHECK واحد فقط
    supplier_id uuid NULL FK suppliers,
    party_name varchar(255) NULL,            -- snapshot للطباعة
    payment_terms_id uuid NULL FK payment_terms, due_date date NULL,
    price_list_id uuid NULL FK price_lists,
    currency char(3) NN d='EGP',
    subtotal numeric(18,2) NN d=0, discount_total numeric(18,2) NN d=0,
    tax_total numeric(18,2) NN d=0, total numeric(18,2) NN d=0,
    allocated_total numeric(18,2) NN d=0,

    stock_document_id uuid NULL FK stock_documents,          -- N7
    original_document_id uuid NULL FK commercial_documents,  -- للمرتجع
    source_type varchar(20) NN d='manual' CHECK (source_type IN ('manual','booking','pos')),
    booking_id uuid NULL,                                    -- FK يُضاف في 0029
    CONSTRAINT ck_doc_party CHECK (
        (doc_type IN ('sales_invoice','sales_return')     AND customer_id IS NOT NULL AND supplier_id IS NULL)
     OR (doc_type IN ('purchase_invoice','purchase_return') AND supplier_id IS NOT NULL AND customer_id IS NULL))
);
CREATE UNIQUE INDEX ux_doc_no ON commercial_documents (branch_id, doc_type, doc_no) WHERE doc_no IS NOT NULL;
CREATE UNIQUE INDEX ux_doc_original ON commercial_documents (original_document_id, doc_type)
    WHERE original_document_id IS NOT NULL;
CREATE INDEX ix_doc_branch_date ON commercial_documents (branch_id, doc_date DESC, id DESC);
CREATE INDEX ix_doc_party ON commercial_documents (customer_id, doc_date DESC);
CREATE INDEX ix_doc_supplier ON commercial_documents (supplier_id, doc_date DESC);
CREATE INDEX ix_doc_unposted ON commercial_documents (doc_type, doc_date)
    WHERE status='posted' AND journal_entry_id IS NULL;

CREATE TABLE commercial_document_lines (
    id uuid PK, document_id uuid NN FK commercial_documents ON DELETE CASCADE,
    line_no int NN, item_id uuid NN FK items, unit_id uuid NN FK units,
    conversion_factor numeric(18,6) NN d=1,
    quantity numeric(18,4) NN CHECK > 0,
    unit_price numeric(18,4) NN CHECK >= 0,
    discount_percent numeric(5,2) NN d=0 CHECK BETWEEN 0 AND 100,
    discount_amount numeric(18,2) NN d=0,
    net_amount numeric(18,2) NN d=0,
    tax_id uuid NULL FK taxes, tax_rate numeric(5,2) NN d=0, tax_amount numeric(18,2) NN d=0,
    total_amount numeric(18,2) NN d=0,
    unit_cost numeric(18,6) NULL, cost_total numeric(18,2) NULL,   -- D22 snapshot
    booking_line_id uuid NULL,               -- FK في 0029
    returned_qty numeric(18,4) NN d=0,       -- N24
    description varchar(300) NULL, created_at timestamptz NN d=now(),
    UNIQUE (document_id, line_no));
CREATE INDEX ix_doc_lines_item ON commercial_document_lines (item_id, document_id);
```
> الترقيم: `SALES_INVOICE`, `SALES_RETURN`, `PURCHASE_INVOICE`, `PURCHASE_RETURN` في `document_sequences` (N26).


---

## 5) 4C — `0027_phase4c_treasury.sql`

```sql
CREATE TABLE treasuries (                        -- STD + branch
    id uuid PK, is_active bool NN d=true, version int NN d=1, created_at/by, updated_at/by,
    branch_id uuid NN FK branches, code varchar(30) NN, name_ar NN, name_en NULL,
    treasury_type varchar(20) NN CHECK (treasury_type IN ('cash','bank','wallet')),
    account_id uuid NN FK accounts,              -- D11
    opening_balance numeric(18,2) NN d=0,
    UNIQUE (branch_id, code));

CREATE TABLE treasury_methods (                  -- STD
    id uuid PK, is_active bool NN d=true, version int NN d=1, created_at/by, updated_at/by,
    treasury_id uuid NN FK treasuries ON DELETE CASCADE, code varchar(30) NN, name_ar NN, name_en NULL,
    method_type varchar(20) NN CHECK (method_type IN ('cash','card','wallet','cheque','transfer')),
    clearing_account_id uuid NULL FK accounts,   -- D12: حساب وسيط
    fee_percent numeric(5,2) NN d=0, fee_fixed numeric(18,2) NN d=0,
    requires_reference boolean NN d=false,
    UNIQUE (treasury_id, code));

CREATE TABLE vouchers (                          -- DOC + REV
    id uuid PK, version int NN d=1, created_at/by, updated_at/by,
    branch_id uuid NN FK branches, doc_no varchar(30) NULL, doc_date date NN,
    status varchar(10) NN d='draft' CHECK (status IN ('draft','posted','cancelled')),
    notes varchar(500), posted_at, posted_by, journal_entry_id uuid NULL FK journal_entries,
    reversed_at, reversed_by, reverse_reason varchar(300),
    voucher_type varchar(10) NN CHECK (voucher_type IN ('receipt','payment','transfer')),
    party_type char(1) NULL CHECK (party_type IN ('C','S')),
    party_id uuid NULL, party_name varchar(255) NULL,
    treasury_id uuid NN FK treasuries, to_treasury_id uuid NULL FK treasuries,
    total_amount numeric(18,2) NN d=0, fee_total numeric(18,2) NN d=0,
    CONSTRAINT ck_voucher_party CHECK (voucher_type='transfer' OR party_id IS NOT NULL));
CREATE UNIQUE INDEX ux_voucher_no ON vouchers (branch_id, voucher_type, doc_no) WHERE doc_no IS NOT NULL;

CREATE TABLE voucher_lines (
    id uuid PK, voucher_id uuid NN FK vouchers ON DELETE CASCADE, line_no int NN,
    treasury_method_id uuid NN FK treasury_methods,
    amount numeric(18,2) NN CHECK > 0, fee_amount numeric(18,2) NN d=0,
    reference varchar(100) NULL, created_at timestamptz NN d=now(),
    UNIQUE (voucher_id, line_no));

CREATE TABLE document_allocations (               -- N12
    id uuid PK, source_voucher_id uuid NN FK vouchers ON DELETE CASCADE,
    target_document_id uuid NN FK commercial_documents,
    amount numeric(18,2) NN CHECK > 0, allocated_at timestamptz NN d=now(),
    created_by uuid NULL FK users);
CREATE UNIQUE INDEX ux_alloc ON document_allocations (source_voucher_id, target_document_id);

CREATE TABLE cashier_shifts (                     -- STD
    id uuid PK, is_active bool NN d=true, version int NN d=1, created_at/by, updated_at/by,
    branch_id uuid NN FK branches, treasury_id uuid NN FK treasuries, user_id uuid NN FK users,
    code varchar(30) NN, opened_at timestamptz NN d=now(), closed_at timestamptz NULL,
    opening_cash numeric(18,2) NN d=0, expected_cash numeric(18,2) NN d=0,
    counted_cash numeric(18,2) NULL, variance numeric(18,2) NULL,
    status varchar(10) NN d='open' CHECK (status IN ('open','closed','reopened')),
    closed_by uuid NULL FK users, reopened_by uuid NULL FK users, notes varchar(500),
    UNIQUE (branch_id, code));
CREATE UNIQUE INDEX ux_shift_open ON cashier_shifts (treasury_id) WHERE status='open';

CREATE TABLE shift_denominations (
    id uuid PK, shift_id uuid NN FK cashier_shifts ON DELETE CASCADE,
    denomination numeric(18,2) NN CHECK > 0, count int NN CHECK >= 0,
    UNIQUE (shift_id, denomination));
```
**مفاتيح الترحيل المضافة هنا:** `customer_advances` (C) · `supplier_advances` (S) · `payment_fees` · `cash_over_short` · `inter_branch_clearing`.


---

## 6) 4D — `0030_phase4d_bookings.sql`

```sql
CREATE EXTENSION IF NOT EXISTS btree_gist;   -- D10

CREATE TABLE bookings (                       -- DOC + REV
    id uuid PK, version int NN d=1, created_at/by, updated_at/by,
    branch_id uuid NN FK branches, doc_no varchar(30) NULL, booking_date date NN,
    status varchar(20) NN d='draft' CHECK (status IN
        ('draft','confirmed','partially_delivered','completed','cancelled','expired')),
    notes varchar(500), posted_at, posted_by, journal_entry_id uuid NULL FK journal_entries,
    reversed_at, reversed_by, reverse_reason varchar(300),
    customer_id uuid NN FK customers, source varchar(10) NN d='manual' CHECK (source IN ('manual','pos')),
    due_date date NULL, expires_at timestamptz NULL,
    assigned_employee_id uuid NULL FK employees,
    related_booking_id uuid NULL FK bookings,   -- D3
    total_amount numeric(18,2) NN d=0, deposit_amount numeric(18,2) NN d=0);
CREATE UNIQUE INDEX ux_booking_no ON bookings (branch_id, doc_no) WHERE doc_no IS NOT NULL;
CREATE INDEX ix_bookings_customer ON bookings (customer_id, status, booking_date DESC);
CREATE INDEX ix_bookings_employee ON bookings (assigned_employee_id, booking_date DESC);

CREATE TABLE booking_lines (
    id uuid PK, booking_id uuid NN FK bookings ON DELETE CASCADE, line_no int NN,
    item_id uuid NN FK items, unit_id uuid NN FK units, warehouse_id uuid NN FK warehouses,
    quantity numeric(18,4) NN CHECK > 0, unit_price numeric(18,4) NN CHECK >= 0,
    discount_percent numeric(5,2) NN d=0, tax_id uuid NULL FK taxes,
    net_amount numeric(18,2) NN d=0, tax_amount numeric(18,2) NN d=0, total_amount numeric(18,2) NN d=0,
    delivered_qty numeric(18,4) NN d=0, invoiced_qty numeric(18,4) NN d=0,
    cancelled_qty numeric(18,4) NN d=0, reserved_qty numeric(18,4) NN d=0,
    created_at timestamptz NN d=now(), UNIQUE (booking_id, line_no));

CREATE TABLE booking_assignments (            -- D8: سجل تاريخي
    id uuid PK, booking_id uuid NN FK bookings ON DELETE CASCADE,
    employee_id uuid NN FK employees, role varchar(10) NN CHECK (role IN ('owner','executor')),
    assigned_at timestamptz NN d=now(), unassigned_at timestamptz NULL,
    assigned_by uuid NULL FK users, notes varchar(300));

CREATE TABLE appointments (                   -- STD
    id uuid PK, is_active bool NN d=true, version int NN d=1, created_at/by, updated_at/by,
    branch_id uuid NN FK branches, employee_id uuid NN FK employees,
    booking_id uuid NULL FK bookings, customer_id uuid NULL FK customers,
    starts_at timestamptz NN, ends_at timestamptz NN,
    appointment_range tstzrange GENERATED ALWAYS AS (tstzrange(starts_at, ends_at, '[)')) STORED,
    status varchar(15) NN d='scheduled' CHECK (status IN
        ('scheduled','confirmed','in_progress','done','cancelled','no_show')),
    notes varchar(500), CONSTRAINT ck_appt_range CHECK (ends_at > starts_at));
ALTER TABLE appointments ADD CONSTRAINT ex_appt_no_overlap
    EXCLUDE USING gist (employee_id WITH =, appointment_range WITH &&)
    WHERE (status <> 'cancelled');            -- D10
CREATE INDEX ix_appt_day ON appointments (branch_id, starts_at);

CREATE TABLE delivery_notes (                 -- DOC
    id uuid PK, version int NN d=1, created_at/by, updated_at/by,
    branch_id uuid NN FK branches, doc_no varchar(30) NULL, doc_date date NN,
    status varchar(10) NN d='draft' CHECK (status IN ('draft','posted','cancelled')),
    notes varchar(500), posted_at, posted_by, journal_entry_id uuid NULL FK journal_entries,
    booking_id uuid NN FK bookings, warehouse_id uuid NN FK warehouses,
    stock_document_id uuid NULL FK stock_documents,     -- N7
    invoice_id uuid NULL FK commercial_documents);      -- D4

CREATE TABLE delivery_note_lines (
    id uuid PK, delivery_note_id uuid NN FK delivery_notes ON DELETE CASCADE, line_no int NN,
    booking_line_id uuid NN FK booking_lines, item_id uuid NN FK items, unit_id uuid NN FK units,
    quantity numeric(18,4) NN CHECK > 0, unit_cost numeric(18,6) NN d=0, cost_total numeric(18,2) NN d=0,
    stock_document_line_id uuid NULL FK stock_document_lines,
    created_at timestamptz NN d=now(), UNIQUE (delivery_note_id, line_no));

CREATE TABLE service_consumptions (           -- D14/D15
    id uuid PK, delivery_note_id uuid NN FK delivery_notes ON DELETE CASCADE,
    booking_line_id uuid NN FK booking_lines, recipe_item_id uuid NULL FK service_recipe_items,
    component_item_id uuid NN FK items, unit_id uuid NN FK units,
    quantity numeric(18,4) NN CHECK > 0, unit_cost numeric(18,6) NN d=0, cost_total numeric(18,2) NN d=0,
    stock_document_line_id uuid NULL FK stock_document_lines, created_at timestamptz NN d=now());
```


---

## 7) 4E — `0031_phase4e_sales.sql`

```sql
ALTER TABLE commercial_documents ADD COLUMN booking_id uuid NULL REFERENCES bookings;
ALTER TABLE commercial_documents ADD COLUMN delivered_at timestamptz NULL;
ALTER TABLE commercial_document_lines ADD COLUMN booking_line_id uuid NULL REFERENCES booking_lines;

CREATE TABLE sales_return_links (          -- N24: تتبّع الكميات المرتجعة
    id uuid PK, return_line_id uuid NN UNIQUE FK commercial_document_lines ON DELETE CASCADE,
    original_line_id uuid NN FK commercial_document_lines,
    quantity numeric(18,4) NN CHECK > 0, created_at timestamptz NN d=now());
```
**قيد D3 عبر Trigger (لا CHECK):** PostgreSQL لا تسمح بـSubquery داخل `CHECK`، لذا:
```sql
CREATE FUNCTION fn_sales_line_booking_guard() RETURNS trigger ...
-- BEFORE INSERT/UPDATE ON commercial_document_lines:
--   doc_type ∈ (sales_invoice, sales_return) و NEW.booking_line_id IS NULL
--   → RAISE 'SALES_LINE_REQUIRES_BOOKING'
CREATE TRIGGER trg_sales_line_booking_guard BEFORE INSERT OR UPDATE ON commercial_document_lines
    FOR EACH ROW EXECUTE FUNCTION fn_sales_line_booking_guard();
```
**Trigger حارس المرتجع:** `sum(sales_return_links.quantity)` لكل `original_line_id` ≤ `original_line.quantity`، ويُحدِّث `returned_qty` على بند الأصل.

---

## 8) 4F — `0032_phase4f_settlements.sql`

```sql
ALTER TABLE app_settings ADD COLUMN commission_basis smallint NN d=1 CHECK BETWEEN 1 AND 3;
-- 1 = صافي الفاتورة · 2 = إجمالي الفاتورة · 3 = المُحصّل (D20)

CREATE TABLE booking_settlements (          -- STD + DOC
    id uuid PK, version int NN d=1, created_at/by, updated_at/by,
    branch_id uuid NN FK branches, doc_no varchar(30) NULL, doc_date date NN,
    status varchar(10) NN d='draft' CHECK (status IN ('draft','posted','cancelled')),
    notes varchar(500), posted_at, posted_by, journal_entry_id uuid NULL FK journal_entries,
    booking_id uuid NN FK bookings, settlement_type varchar(25) NN CHECK (settlement_type IN
        ('refund','cancel_qty','refund_and_cancel','transfer','forfeit')),
    amount numeric(18,2) NN d=0, refund_voucher_id uuid NULL FK vouchers,
    target_booking_id uuid NULL FK bookings, reason varchar(300),
    approved_by uuid NULL FK users, approved_at timestamptz NULL);

CREATE TABLE employee_commissions (
    id uuid PK, employee_id uuid NN FK employees,
    document_id uuid NN FK commercial_documents, document_line_id uuid NULL FK commercial_document_lines,
    basis smallint NN CHECK BETWEEN 1 AND 3, base_amount numeric(18,2) NN, rate numeric(5,2) NN,
    amount numeric(18,2) NN, period_month date NN, created_at timestamptz NN d=now(),
    UNIQUE (employee_id, document_line_id, period_month));
CREATE INDEX ix_commissions_period ON employee_commissions (period_month, employee_id);
```
**مفاتيح الترحيل المضافة هنا:** `booking_forfeit_income` (bookings).

---

## 9) 4G/4H — `0033_phase4g_pos.sql` و`0034_phase4h_followups.sql`

```sql
-- 4G: لا جداول جديدة. POS = bookings(source='pos') + commercial_documents(source_type='pos') + vouchers
CREATE INDEX ix_doc_pos ON commercial_documents (booking_id) WHERE source_type='pos';

-- 4H
CREATE TABLE follow_ups (
    id uuid PK, is_active bool NN d=true, version int NN d=1, created_at/by, updated_at/by,
    customer_id uuid NN FK customers, booking_id uuid NULL FK bookings,
    appointment_id uuid NULL FK appointments, assigned_user_id uuid NULL FK users,
    due_at timestamptz NN, channel varchar(20) NN d='call' CHECK (channel IN ('call','whatsapp','sms','visit')),
    note varchar(500), status varchar(10) NN d='pending' CHECK (status IN ('pending','done','cancelled')),
    completed_at timestamptz NULL);
CREATE INDEX ix_followups_due ON follow_ups (status, due_at) WHERE status='pending';
```


---

## 10) المرحلة 6 — `0035_phase6_reports_notifications.sql`

```sql
CREATE TABLE saved_report_filters (         -- N23
    id uuid PK, user_id uuid NN FK users ON DELETE CASCADE,
    report_key varchar(100) NN, filter_name varchar(150) NN,
    filter_payload_json jsonb NN CHECK (octet_length(filter_payload_json::text) <= 8192),
    is_default boolean NN d=false, created_at timestamptz NN d=now(), updated_at timestamptz NULL,
    UNIQUE (user_id, report_key, filter_name));

CREATE TABLE notifications (                -- N22
    id uuid PK, user_id uuid NULL FK users ON DELETE CASCADE, role_id uuid NULL FK roles ON DELETE CASCADE,
    title varchar(200) NN, message text NN,
    type varchar(50) NN d='info' CHECK (type IN ('info','warning','error','success')),
    action_url varchar(300) NULL, is_read boolean NN d=false, read_at timestamptz NULL,
    created_at timestamptz NN d=now(),
    CONSTRAINT ck_notif_target CHECK (user_id IS NOT NULL OR role_id IS NOT NULL));
CREATE INDEX ix_notif_inbox ON notifications (user_id, is_read, created_at DESC);

CREATE TABLE message_outbox (               -- N21
    id uuid PK, recipient_phone varchar(30) NN, message_body text NN,
    channel varchar(20) NN d='whatsapp' CHECK (channel IN ('whatsapp','sms')),
    status varchar(20) NN d='pending' CHECK (status IN ('pending','sent','failed','cancelled')),
    retry_count int NN d=0, max_retries int NN d=3, last_error text NULL,
    next_attempt_at timestamptz NN d=now(), dedupe_key varchar(120) NULL,
    provider_message_id varchar(120) NULL, scheduled_at timestamptz NN d=now(),
    sent_at timestamptz NULL, created_at timestamptz NN d=now());
CREATE UNIQUE INDEX ux_outbox_dedupe ON message_outbox (dedupe_key) WHERE dedupe_key IS NOT NULL;
CREATE INDEX ix_outbox_due ON message_outbox (next_attempt_at) WHERE status='pending';

CREATE TABLE sales_daily_totals (           -- N16: مصدر لوحة المؤشرات
    id uuid PK, branch_id uuid NN FK branches, business_date date NN,
    invoices_count int NN d=0, sales_total numeric(18,2) NN d=0, tax_total numeric(18,2) NN d=0,
    returns_total numeric(18,2) NN d=0, collections_total numeric(18,2) NN d=0,
    updated_at timestamptz NN d=now(), UNIQUE (branch_id, business_date));
```
**`bi` (Materialized Views + صلاحيات):**
```sql
CREATE SCHEMA IF NOT EXISTS bi;
CREATE MATERIALIZED VIEW bi.mv_daily_sales_summary AS ... ;
CREATE MATERIALIZED VIEW bi.mv_customer_aging AS ... ;
CREATE MATERIALIZED VIEW bi.mv_inventory_status AS ... ;
CREATE UNIQUE INDEX ON bi.mv_daily_sales_summary (branch_id, business_date);  -- للـREFRESH CONCURRENTLY
CREATE ROLE bi_reader LOGIN PASSWORD '...' NOSUPERUSER NOCREATEDB;
GRANT USAGE ON SCHEMA bi TO bi_reader;
GRANT SELECT ON ALL TABLES IN SCHEMA bi TO bi_reader;
ALTER DEFAULT PRIVILEGES IN SCHEMA bi GRANT SELECT ON TABLES TO bi_reader;   -- N16
```

---

## 11) المرحلة 7 — `0036_phase7_operations.sql`

```sql
CREATE TABLE backup_log (
    id uuid PK, database_name varchar(100) NN, backup_type varchar(20) NN
        CHECK (backup_type IN ('daily','manual','pre_upgrade')),
    file_path varchar(500) NN, size_bytes bigint NN d=0,
    started_at timestamptz NN d=now(), finished_at timestamptz NULL,
    status varchar(10) NN d='running' CHECK (status IN ('running','success','failed')),
    error text NULL, verified_at timestamptz NULL, created_by uuid NULL FK users);

CREATE TABLE import_jobs (
    id uuid PK, entity_type varchar(50) NN, file_name varchar(250) NN,
    status varchar(20) NN d='pending'
        CHECK (status IN ('pending','validated','processing','completed','failed','cancelled')),
    total_rows int NN d=0, processed_rows int NN d=0, failed_rows int NN d=0,
    error_log_json jsonb NULL, mapping_json jsonb NULL,
    created_by uuid NN FK users, created_at timestamptz NN d=now(), finished_at timestamptz NULL);
CREATE INDEX ix_import_jobs_status ON import_jobs (status, created_at DESC);
```
**Control DB (سكريبت منفصل يُدار بـ`psql`/أداة 7A):**
```sql
CREATE TABLE tenants (
    id uuid PK, tenant_code varchar(50) NN UNIQUE,   -- مطابق للـsubdomain
    organization_name varchar(200) NN,
    database_name varchar(100) NN UNIQUE,            -- N14: بلا كلمات مرور
    host varchar(100) NULL, port int NULL,
    is_active boolean NN d=true, subscription_plan varchar(50) NN d='standard',
    created_at timestamptz NN d=now(), activated_at timestamptz NULL, deactivated_at timestamptz NULL);
-- لا tenant_migrations: تُقرأ schemaversions من قاعدة كل عميل (N14/N2)
```

