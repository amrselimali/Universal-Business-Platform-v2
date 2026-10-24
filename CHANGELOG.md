# CHANGELOG

## 2026-10-24 — Phase 4H: customer follow-ups

### Added
- Migration `0034`: `follow_ups` (customer/booking/appointment link, channel, due date, status)
  with a partial unique index preventing two pending follow-ups for the same booking.
- `FollowUpRepository` + `FollowUpService` (create, complete with version check, and idempotent
  generation for overdue confirmed bookings) and `sales.followups.*` permissions.
- Screen `/sales/follow-ups` (add, complete, generate overdue) with translations.
- 2 tests (120 total): create/complete and one-time overdue generation.

## 2026-10-24 — Phase 4G: point of sale

### Added
- Migration `0033`: POS booking index and receipt-voucher date index (no new tables).
- `PosRepository.CheckoutAsync`: one transaction creates a confirmed POS booking
  (`source = 'pos'`), its delivery note, issues stock through the FEFO engine, posts the COGS
  entry, auto-creates/posts the sales invoice, posts the receipt voucher and allocates it to the
  invoice — any failure rolls everything back.
- `PosService` with `pos.terminal.view/checkout` and `pos.receipts.view/print` permissions + audit.
- Screens: `/pos/terminal` (quick sale with customer, item, quantity, price, treasury method and a
  receipt summary) and `/pos/receipts` (POS invoices), with translations.
- 2 tests (118 total): full checkout creates booking/delivery/invoice/voucher and balances, and a
  stock failure leaves no booking, invoice or voucher behind.

### Known limitations (4G)
- Terminal supports a single line per checkout; multi-line cart and barcode search are next-step UI.

## 2026-10-24 — Phase 4F: settlements, commissions, aging and posting center

### Added
- Migration `0032`: `app_settings.commission_basis`, `booking_settlements` (five types +
  approval + treasury link), `employee_commissions`, the `booking_forfeit_income` posting key,
  and the `v_unposted_documents` view (commercial documents, vouchers, delivery notes).
- Settlements: refund posts `Dr customer advances / Cr treasury`, forfeit posts
  `Dr customer advances / Cr forfeit income`, quantity cancel releases reservations and cancels
  the booking (or marks it partially delivered when something was delivered), transfer moves the
  deposit to another booking of the same customer; approval is mandatory for any amount.
- Commissions (`D20`): monthly statement per employee and invoice line with three bases
  (1 = net, 2 = gross, 3 = collected) and idempotent recalculation.
- Four aging reports: customers, customer advances, open bookings (reusing the 4B supplier aging).
- Posting center: unposted documents from the new view plus three reconciliations
  (stock vs GL, customers vs GL, suppliers vs GL).
- `SalesFinanceService` with `sales.settlements.*`, `sales.commissions.*`, `reports.aging.*` and
  `accounting.posting.*` permissions and audit entries.
- Screens: `/sales/settlements` (+ dialog with approval/posting), `/sales/commissions`,
  `/reports/aging`, `/accounting/posting-center`, with translations.
- 6 tests (116 total): forfeit entry, refund entry, cancel releasing reservations + cancelling,
  approval requirement, the three commission bases, and aging/unposted/reconciliation reads.

### Known limitations (4F)
- Settlement documents carry no stock movement; quantity cancellation uses the booking’s current
  reserved quantities (not a per-line quantity selector yet).
- Commission “collected” basis uses the invoice-level allocated amount repeated per line.

## 2026-10-24 — Phase 4E: sales invoices and returns

### Added
- Migration `0031`: `booking_id`/`delivered_at` on `commercial_documents`, `booking_line_id` on
  `commercial_document_lines`, `sales_return_links`, the D3 guard (`SALES_LINE_REQUIRES_BOOKING`),
  the N24 return-quantity guard (`RETURN_QTY_EXCEEDED` + `returned_qty` maintenance), and a narrow
  line-guard exception that allows updating only `returned_qty` on a posted line.
- `SalesInvoiceRepository`: drafts from delivered booking remainders or returnable invoice lines,
  posting a sales invoice (`Dr receivable / Cr revenue + VAT`), automatic consumption of open
  receipt vouchers (`Dr advances / Cr receivable`) updating `allocated_total`, and updating
  `booking_lines.invoiced_qty`.
- Sales returns: credit note (`Dr revenue + VAT / Cr receivable`) plus stock re-entry at the
  original delivery cost with `Dr inventory / Cr COGS`, inside one transaction.
- Auto invoice on delivery (`auto_invoice_on_delivery` + `invoice_grouping_mode = 0`) created and
  posted inside the delivery transaction and linked through `delivery_notes.invoice_id`.
- `SalesInvoiceService` with `sales.invoices.*` and `sales.returns.*` permissions and audit.
- Screens: `/sales/invoices` (+ dialog from booking remainders) and `/sales/returns`
  (+ dialog from posted invoices), with translations.
- 5 tests (110 total): D3 guard, revenue + advance consumption with party-balance reconciliation,
  credit note + restock at original cost + N24 guard, auto invoice on delivery, over-invoicing guard.

### Known limitations (4E)
- The existing unique index `ux_commercial_documents_original` allows a single return document per
  original invoice; multiple partial return documents need a schema decision (recorded for 4F).

## 2026-10-24 — Phase 4D: bookings, appointments, delivery execution

### Added
- Migration `0030`: `bookings`, `booking_lines`, `booking_assignments`, `appointments`
  (with `EXCLUDE USING gist` so one employee cannot have overlapping slots), `delivery_notes`,
  `delivery_note_lines`, `service_consumptions`, plus the `delivery` journal source type.
- `BookingRepository`: draft creation with per-line discount/tax totals, confirmation that
  reserves `stock_balances.reserved_qty` only when `reserve_stock_on_booking` is on, cancellation
  that releases reservations, employee assignment with history (`owner`/`executor`).
- `DeliveryRepository`: draft from booking remainders, posting that releases the reservation,
  issues stock through the FEFO engine, books `Dr COGS / Cr Inventory`, fills the line cost
  snapshot, and moves the booking to `partially_delivered`/`completed`.
- `AppointmentRepository` + day schedule; overlapping slots are rejected by the database
  (`23P01` → `validation.appointment_overlap`) and cancellations free the slot.
- `BookingService` and `SalesFulfillmentService` with `sales.bookings.*`,
  `sales.appointments.*`, `sales.deliveries.*` permissions and audit entries.
- Screens: `/sales/bookings` (+ create/assign/cancel dialogs), `/sales/appointments`
  (+ dialog and status actions), `/sales/deliveries` (+ delivery dialog and posting).
- 5 tests (105 total): reservation and over-booking block, delivery + COGS + completion,
  cancellation releasing reservations, appointment overlap/cancel, assignment history.

### Known limitations (4D remainder)
- Service recipe consumption (`consumption_mode` + substitutes) is not wired yet; the table and
  contract exist. Auto-invoice on delivery (`D4`) lands with 4E.

## 2026-10-24 — Phase 4C-ب: clearing reconciliation, voucher cancel, shift reopen, allocation UI

### Added
- Migration `0029`: clearing-account index for `treasury_methods`, plus `reopened_at` and
  `reopen_reason` on `cashier_shifts`.
- `/treasury/reconciliation`: clearing balances (cards/wallets/cheques) aggregated from
  `gl_balances`, with a settlement dialog that posts one balanced entry between the clearing
  account and the selected treasury (`treasury.reconciliation.view/post`).
- Voucher cancel for drafts (`treasury.vouchers.cancel`) — posted vouchers stay immutable.
- Allocation dialog on posted receipts/payments: lists only open invoices of the party,
  shows the remaining voucher amount, and calls the existing allocation engine.
- Shift reopen with a mandatory reason (`treasury.shifts.reopen`): reverses the variance entry
  through `fn_gl_reverse_entry`, returns the shift to `open` and keeps the single-open-shift
  invariant; the reason is audited.
- 4 tests (100 total): clearing settlement zeroes the account, cancel rules, reopen/reclose with
  reversal, and allocation listing limits.

### Changed
- `TreasuryService` split into partial files (core, vouchers, shifts, reconciliation) to keep the
  300-line limit; `TreasuryRepository` gained `Reconcile.cs` and `Reopen.cs`.

## 2026-10-24 — Phase 4C: treasuries, payment methods, vouchers & cashier shifts

### Added
- Migration `0027`: `treasuries` (cash/bank/wallet linked to a GL account), `treasury_methods`
  (cash/card/wallet/cheque/transfer with clearing account and percent/fixed fees), `vouchers` +
  `voucher_lines` (multi-method receipt/payment/transfer), `document_allocations`,
  `cashier_shifts` + `shift_denominations`, immutability triggers after posting, and five new
  `posting_default_types` keys (`customer_advances`, `supplier_advances`, `payment_fees`,
  `cash_over_short`, `inter_branch_clearing`).
- Migration `0028`: `voucher`/`cash_shift` journal source types, treasury reporting indexes,
  `allocated_total <= total` check, and a narrow guard exception that lets a posted invoice
  update only `allocated_total` when a payment is allocated.
- `TreasuryRepository` (partial files): voucher drafting with automatic fee calculation and
  reference enforcement, atomic posting to the cash/bank journal, allocation with voucher and
  document limits, shift open/close with expected-cash computation, denomination grid.
- Posting rules: receipt = Dr treasury/clearing + Dr payment fees / Cr customer advances;
  payment = Dr supplier advances + Dr fees / Cr treasury/clearing; transfer = Dr destination /
  Cr source, with `inter_branch_clearing` plus a second entry when the branches differ;
  allocation = Dr advances / Cr receivable (reverse for suppliers); shift variance goes to
  `cash_over_short`.
- `TreasuryService` with `treasury.management.*`, `treasury.vouchers.*` and `treasury.shifts.*`
  permissions and audit entries.
- Screens: `/treasury/management` (+ treasury and payment-method dialogs), `/treasury/vouchers`
  (multi-line editor with live totals), `/treasury/shifts` (open/close + denomination counting).
- 6 tests (96 total): multi-method receipt with a 2% card fee, allocation limits and party-balance
  reconciliation, inter-branch transfer clearing, voucher immutability, one open shift per
  treasury, and shift variance posting.

### Changed
- Posting readiness covers the treasury module (5 keys, 2 required).
- Journal `source_type` check accepts `voucher` and `cash_shift`.

## 2026-10-22 — Phase 3: accounting core & general ledger

### Added
- Migrations `0020`–`0023`: `accounts` (materialized path tree), `cost_centers`,
  `journals` (7 system journals), `posting_defaults`, `item_category_accounts`,
  `journal_entries`, `journal_entry_lines`, `gl_balances`, `party_balances`,
  plus `app_settings.auto_post_stock_journals` and `app_settings.accounting_start_date`.
- Atomic posting in SQL: `fn_gl_post_entry` (numbering + balance/period/party/cost-center
  checks + monthly & party balances in one transaction) and `fn_gl_reverse_entry`
  (reversing entry + `is_reversed` flag, double reversal impossible).
- Immutability enforced by triggers: posted entries and their lines cannot be edited or
  deleted; only the reversal flag may change.
- 13 screens: chart of accounts, cost centers, journals, accounting settings (defaults,
  category accounts, completeness check), journal entries list, entry editor (party picker,
  cost distribution, auto-balance, print), opening balances (CSV import + equity balancing),
  stock posting center, and 5 reports (ledger, trial balance, party statement, cost-center
  analysis, stock vs GL).
- Permission catalog keys in the form `accounting.<screen>.<action>` via
  `ScreenDefinition.PermissionGroup`, with a test that fails when a screen button is not
  registered in `AppRegistry`.
- Automatic stock journaling from `item_category_accounts` + `posting_defaults`, with
  preview, single/bulk generation, idempotency, rounding handling, and auto post/reverse
  hooks on stock document posting when `auto_post_stock_journals` is enabled.
- 23 new tests (75 total): balance enforcement, immutability, double reversal, party and
  cost-center policies, closed periods, tree move, registry coverage, keyset ledger
  pagination, stock auto-posting, and an opt-in 1M-line performance test.

### Changed
- `StockDocumentService` accepts an optional `StockAccountingService` to auto-post journals
  (warehouse posting never fails because of accounting setup).
- Ledger index rebuilt with `INCLUDE (debit, credit, branch_id)` so period totals are
  served index-only.


## 2026-10-21 — Phase 2 gap closure (0018)

### Added
- `stocktake_lines.reason` + trigger `STOCKTAKE_REASON_REQUIRED`; the service requires a reason
  whenever the counted quantity differs from the book quantity.
- Period-close checks: unfinished transfers, unposted stocktakes, zero-cost balances
  (in addition to drafts and unbalanced balances), for branch and final closures.
- Custom fields for `items`, `customers` and `suppliers`, rendered automatically in their forms.
- Periods screen filters (search + status).
- 6 new tests (52 total).


## 2026-10-21 — Phase 2: inventory, partners, period engine

### Added
- Migrations `0012`–`0017`: document-sequence v2, units/items/barcodes, customers/suppliers
  with company-or-branch scope, unified period engine, stock documents/movements/balances,
  transfer receipts, and stocktakes.
- 21 tables, 9 screens (`/inventory/*`, `/partners/*`, `/accounting/periods`, `/reports/stock`).
- Atomic stock posting with moving weighted average, negative-stock guard, reversal documents,
  partial transfer receiving, stocktake adjustments and period-close checks/events.
- Permission actions `POST` · `REVERSE` · `RECEIVE` · `CLOSE_PERIOD` · `REOPEN_PERIOD`.
- 14 new automated tests (46 total) covering posting, costing, periods, transfers, stocktake.

### Fixed
- `0011` sequence prefix was `varchar(10)` while `STOCK_RECEIPT` needs 13 → 4 failing tests.
  `0012` widens it and adds padding/central numbering (`branch_id` nullable).
- Document numbers are unique per branch (`branch_id, doc_no`); stocktake codes per branch.
- Deleting a draft document/stocktake with lines failed because the line guard saw a missing
  parent during cascade; the guard now allows cascade deletes.

## 2026-10-21 — Phase 2a: settings + document numbering

### Added
- Migration `0011_phase2a_settings_and_sequences.sql`: `app_settings.allow_negative_stock`,
  table `document_sequences`, function `next_document_number`.
- `DocumentSequenceRepository` (atomic numbering inside the caller's transaction).
- "Allow negative stock" switch on the General Settings screen (ar/en).
- `document_sequences` listed (read-only) in the Database admin screen.

### Fixed
- General Settings screen edited the cached settings instance directly; it now edits a clone.

## 2026-10-19 — v0.1.0 Phase 1

### Added
- Standalone solution: Core / Infrastructure / Application / Web / Tests.
- PostgreSQL schema with 21 tables, DbUp migrations, seeds, indexes, Arabic search,
  optimistic concurrency, immutable audit log, and warehouse/branch consistency guards.
- Authentication: cookie UI + JWT API + refresh tokens (hash only) + server-side sessions,
  lockout, uniform login errors, CSRF protection.
- Authorization: default-deny registry, roles, per-user overrides, scope, branch isolation.
- Screens: login, dashboard, settings, branches, warehouses, currencies, custom fields,
  users and permission matrix, roles, audit, QA center, database admin.
- Responsive Arabic/English RTL/LTR UI with MudBlazor.
- Dockerfile, docker-compose, GitHub Actions workflow, Arabic documentation.
- 25 automated tests.

### Security
- Sensitive audit fields (password/token hashes) are stripped before persistence.
- Super Admin protections, last-super-admin guard, and read-only audit triggers.
- QA refuses to run against the application database.
