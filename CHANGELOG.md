# CHANGELOG

## 2026-10-31 — Phase 6B: `bi` analytics schema (read-only, refreshed)

### Added
- Migration `0043`: schema `bi` with three materialized views — `sales_daily`,
  `stock_value_by_warehouse`, `party_balances_monthly` — each with a **unique index** (required by
  `REFRESH … CONCURRENTLY`), plus `bi.fn_refresh_all()` for a plain refresh.
- Role `bi_reader` (`NOLOGIN`) with `USAGE` on `bi`, `SELECT` on its objects and
  `ALTER DEFAULT PRIVILEGES`; it has **no** rights on `public` (asserted by tests).
- `BiRepository`: read the views, `RefreshAsync` (plain) and `RefreshConcurrentlyAsync`
  (three statements, each in its own implicit transaction).
- `BiRefreshWorker` (`BackgroundService`, `Bi:Enabled/IntervalMinutes`, default every 15 min) that
  refreshes concurrently so readers are never blocked.

### Tests
- 4 new tests (220 total, 0 failures): views reflect the source after refresh, concurrent refresh
  works and picks up new rows, the stock-value view is readable, and `bi_reader` can select inside
  `bi` but cannot insert/update/delete there nor read `public.users`.
- Live check: migration `0043` applied, `bi.sales_daily` matched the source row count after the
  worker's startup refresh, and `bi_reader` privileges were `t` for `bi` and `f` for `public.users`.


## 2026-10-31 — Phase 6E: message outbox + retry worker

### Added
- Migration `0042`: `message_outbox` with channel/status checks, a **partial unique index on
  `dedupe_key`** (no duplicate messages on retry), and a partial index on the due queue.
- `MessageOutboxRepository`: idempotent `EnqueueAsync`, `ClaimDueAsync` using
  `FOR UPDATE SKIP LOCKED` with `next_attempt_at` as a lease, `MarkSent`/`MarkFailed`, status counts.
- `OutboxService` (validation before the queue) and `IMessageSender` with a `LoggingMessageSender`
  demo provider that masks the recipient — the single seam to replace with a real SMS/WhatsApp
  provider (X-15).
- `OutboxDispatcher` (`BackgroundService`): claims a batch, sends, marks sent, or records the failure
  with exponential backoff (60s, 120s, 240s … capped at 1h) until `MaxAttempts` makes it `FAILED`.
  Configurable through the `Outbox` section (`Enabled/PollSeconds/BatchSize/MaxAttempts`).

### Tests
- 12 new tests (216 total, 0 failures): dedupe replay, validation, lease-once semantics,
  lease expiry, backoff scheduling, terminal failure after max attempts, exponential backoff table,
  plus dispatcher success/failure paths against a fake provider.
- Live check: migration `0042` applied, a queued WhatsApp message was picked up by the running
  worker and moved to `SENT` with `sent_at` set, and replaying the same `dedupe_key` inserted nothing.


## 2026-10-31 — Phase 6C: sales daily totals + dashboard under 300 ms

### Added
- Migration `0041`: `sales_daily_totals` keyed by `(day, branch_id)` with invoice/return counters,
  net/tax/gross/COGS columns and a `day` index.
- The totals row is upserted **inside the invoice posting transaction**
  (`SalesInvoiceRepository.DailyTotals.cs`), so it commits or rolls back with the posting; returns
  subtract the same aggregates (`sign = -1`).
- `DashboardRepository` reads only aggregates: today/month sales from `sales_daily_totals`, stock
  value from `stock_balances` (branch-scoped through `warehouse_branches`) and receivables from
  `party_balances` — constant-cost queries independent of invoice volume.
- `DashboardService` (dashboard permission + branch scope) and KPI cards on `/` and the new
  `/dashboard` route: sales today, invoices today, month sales, gross margin, stock value,
  receivables.

### Tests
- 3 new tests (204 total, 0 failures): posting updates the totals and the dashboard, a failed posting
  leaves **no** totals row (atomicity), and a measured read over 180 days × 3 branches stays under
  the 300 ms budget while matching the SQL expectations.
- Live check: migration `0041` applied, `/dashboard` and `/` answer `200` with the KPI block.


## 2026-10-31 — Phase 6D: internal notifications (bell, center, broadcaster)

### Added
- Migration `0040`: `notifications` targeting **either** a user **or** a role (XOR check), severity
  check, JSON-object payload check, and partial indexes for unread lookups.
- `NotificationRepository`: visibility = direct + role-based (`user_roles`), unread counter,
  mark-read / mark-all-read / clear, and role member resolution.
- `NotificationService`: permission-guarded read/mark/clear plus a trusted internal `SendAsync` used
  by other services; the bell counter degrades to zero (no exception) for users without the screen.
- `NotificationBroadcaster` (singleton): per-user subscriptions returning `IDisposable`, publishing
  to every open circuit, and releasing handlers on dispose (no leak — asserted by a test).
- UI: bell with an unread badge in `HeaderBar` (subscribes/disposes correctly) and
  `/notifications/center` with icon/severity, mark-all-read, clear-all and live refresh.
- Registered as the `NOTIFICATIONS` screen (`VIEW/MARK_AS_READ/CLEAR_ALL`).

### Tests
- 6 new tests (201 total, 0 failures): direct + role visibility (members only), unread counter,
  mark-read/mark-all/clear, target XOR validation, broadcaster delivery + dispose release, and
  role broadcast reaching every member.
- Live check: migration `0040` applied, `/notifications/center` answers `200`, and the bell link
  renders in the header.


## 2026-10-31 — Phase 6A: reports center, saved filters, CSV/XLSX export

### Added
- Migration `0039`: `saved_report_filters` with `UNIQUE (user_id, report_key, filter_name)`,
  a JSON-object check constraint, a report-key format check and a lookup index.
- `ReportCatalog` (Core) is the single approved list of reports, each bound to the screen code that
  guards it, so `/reports/center` only offers what the user may open.
- `ReportCenterService`: list approved reports by permission, save/apply/delete per-user filters
  (upsert instead of duplicates), and export stock balances to CSV/XLSX.
- `ReportExporter`: RFC4180 CSV with a UTF-8 BOM (Arabic opens correctly in Excel) and a hand-built
  OOXML `xlsx` package via `System.IO.Compression` only — no `System.IO.Packaging`, no new package
  (X-01). Numeric cells are written as numbers, strings are XML-escaped, sheet names are sanitised
  and truncated to 31 characters.
- `/reports/center` screen: report tiles, warehouse/zero-balance filter, CSV/XLSX/print buttons and
  saved-filter management, registered in `AppRegistry` with `VIEW/SAVE_FILTER/DELETE_FILTER/
  EXPORT_CSV/EXPORT_XLSX/PRINT`; `wwwroot/js/reports.js` performs the download.
- `AppRegistry` split into `AppRegistry.Reports.cs` to keep the 300-line cap.

### Tests
- 16 new tests (195 total, 0 failures): export escaping/BOM/zip layout/number cells/column names,
  filter upsert + ownership + validation, and report visibility by permission.
- Live check: migration `0039` applied, `/reports/center` answers `200` with the Arabic title and
  tiles, and `js/reports.js` is served.


## 2026-10-31 — Phase 3 gate + migration 0038 (concurrency, idempotency)

### Added
- Migration `0038`: `stock_documents.idempotency_key` (unique per branch, partial index) and
  `updated_by` on `stock_documents`/`stocktakes`; the append-only ledger tables deliberately stay
  without `updated_by` (documented inside the script).
- Client retries are now safe: `CreateDraftAsync` relies on
  `ON CONFLICT (branch_id, idempotency_key) DO NOTHING` and returns the existing document, and
  `StockDocumentService.SaveAsync` short-circuits a replayed key before writing an audit row.
- Concurrency gate (Phase 3) with 8 scenarios; the close path is now atomic: the closure write and
  the period status update both reject **inside the same SQL statement** when an unposted stock
  document exists in the scope, so posting cannot slip between the check and the closure.
- `IsolatedDatabaseFactory` (`IsolatedDb` in tests): creates, migrates, seeds and drops a dedicated
  database per concurrency scenario, bounded pool included.
- Connection hygiene: `CreateDraftAsync`/`UpdateDraftAsync` no longer hold a write connection while
  reading the document back (this was halving the practical throughput under load).

### Tests
- 14 new tests (179 total, 0 failures): idempotent replay ×4, concurrent double-posting, 50
  concurrent receipts on an isolated database (numbers + balance + value), post-vs-close race ×5,
  company close blocked by an open branch scope, reopen requires a reason and records an event,
  full rollback of a failed posting, refresh-token single use/logout, and Arabic/English key parity.


## 2026-10-31 — Phase 1 + 2.1 + 2.2: forced password change, lookup layer, display formatter

### Added
- Migration `0036`: `users.must_change_password` + `password_changed_at`; the seeded default
  `Admin@123` account is flagged automatically.
- Forced password change on first sign-in: `LoginResult`/`SessionUser`/`ActorContext` carry the flag,
  `PasswordChangeGate` blocks every route and API except `/change-password`, and `MainLayout`
  redirects internal Blazor navigation (a circuit navigation never reaches the middleware).
  Admin-initiated resets (`UserService.ResetPasswordAsync`) now force a change at the next login.
- Migration `0037`: `app_settings.time_zone` (IANA, default `Africa/Cairo`), validated on save
  (`validation.time_zone`) and editable in `/settings/general`.
- `IDisplayFormatter` (date/time/number) reads the configured formats and zone and converts the
  stored UTC values; it replaces every `.ToLocalTime()` in the UI (which depended on the server
  time zone — UTC in the container — and disagreed with the hard-coded `Africa/Cairo` in the audit
  screen).
- `LookupService` in `Ubp.Application`: partners/items/warehouses/item categories for pickers with
  branch scoping from `ActorContext`, replacing direct repository injection in 7 UI files.
- `TableInfo`/`ColumnInfo`/`RowPage` moved from the repository layer to `Ubp.Core.Models`, so the
  database admin screen no longer imports `Ubp.Infrastructure`.
- 3 architectural tests: UI must not import `Ubp.Infrastructure`, `Ubp.Infrastructure` is confined
  to `Startup` and known adapters, and every source file stays ≤ 300 lines (the CI gate now has a
  test-level twin). The first run caught a 7th violating file (`PriceListItemsDialog.razor`).

### Tests
- 24 new tests (165 total, 0 failures) on a real PostgreSQL: forced-change flow, gate rules,
  display formatting/zone fallback, settings round-trip and validation, plus the architecture suite.
- Live verification: login returns `mustChangePassword: true`, a page request answers
  `302 → /change-password`, `/api/v1/system/me` stays allowed, any other API answers
  `403 {"error":"auth.password_change_required"}`, and normal access resumes once the flag clears.


## 2026-10-31 — Phase 0: security, CI/CD and startup guards

### Security
- Removed the live Neon connection string (and its `neondb_owner` password) from
  `src/Ubp.Web/appsettings.json`. Configuration now ships no secrets and no connection defaults;
  local values live in `appsettings.Development.json` and production values come from the
  environment/Secrets.
- `DatabaseOptions` no longer forces `Trust Server Certificate=true` when converting a
  `postgresql://` URL, and it no longer swallows parsing errors. `sslmode` and `channel_binding`
  are mapped explicitly (`require` → `VerifyFull`) — see `docs/EXCEPTIONS.md` X-02.
- `BootstrapOptions.SuperAdminPassword` has no default value any more; `DataSeeder` refuses to
  create the Super Admin when the password is missing or shorter than 12 characters.
- The applied migration `0009_seed.sql` seeds `admin` with the well-known `Admin@123` hash (it is
  never edited — G1). `DataSeeder` now detects that exact hash and replaces it with the configured
  `Bootstrap:SuperAdminPassword`, so the documented knob finally works on a fresh database; when no
  strong password is configured it logs a Critical instead of rotating silently.
- `docker compose`: Postgres is published on `127.0.0.1` only, and `SUPER_ADMIN_PASSWORD` is a
  required variable (no `Admin@123` fallback).
- Startup validation now uses the standard `IValidateOptions<T>` + `ValidateOnStart()` pattern
  (`Ubp.Web/Startup/Validation`) and runs before any database access. Production rejects the
  database owner role for the runtime connection, well-known passwords, development JWT keys and
  a transaction pooler for the migration connection (X-03).
- Public health probe reduced to `{"status":"healthy|unhealthy"}`: no server name, database name or
  table count; it returns `503` when the database is unreachable, caches the result for 15 seconds,
  and a database-free `/health/live` was added (X-07).
- Data Protection key ring can be persisted to disk (`DataProtection:KeyRingPath`), wired in
  `docker-compose.yml` to `/app/keys` so cookie/token keys survive restarts (X-09).

### CI/CD
- `.github/workflows/ci.yml` added — the previous `ci/ci.yml` was outside `.github/workflows`, so
  the pipeline never ran. It keeps the 300-line gate and adds a guard that fails on committed
  build output or committed database credentials, plus concurrency and a job timeout.
- The same file is committed as `docs/ci-workflow.yml` because the automation token used here has
  no `workflows` permission: copy it to `.github/workflows/ci.yml` in the GitHub UI (or push it
  with a token that has the `workflow` scope) to activate the pipeline.

### Repository hygiene
- `.gitattributes` added (line-ending normalisation, binary document types).
- Removed the tracked Word lock file `0- Plan/~$Foundation.docx`.

### Tests
- 10 new tests (9 in `StartupOptionsTests` without a database + 1 asserting the `0009` default
  password is neutralised) — 131 → 141 total.


## 2026-10-24 — Fix: DateTimeOffset offsets when writing timestamptz

### Fixed
- Npgsql 6+ rejects non-zero offsets for `timestamp with time zone`; booking/appointment/follow-up
  times coming from the UI (e.g. +03:00) failed with `Cannot write DateTimeOffset with Offset=03:00`.
- Added `DbTime.Utc(...)` and normalized all user-supplied times in `BookingRepository`,
  `PosRepository` (appointment), `AppointmentRepository` and `FollowUpRepository` before saving;
  display still converts to localime.
- 1 regression test (131 total) writes a +03:00 offset and asserts the stored value is UTC.

## 2026-10-24 — Booking screen: time-only lines, day filter, hidden unauthorized branches

### Changed
- Booking dialog no longer shows quantity, price or totals: each line is a product only, and the
  booking carries a from/to time range (quantity 1 / price 0 behind the scenes).
- Bookings list filters by a selected day (today by default, clearable) and shows the time range.
- Header branch switcher hides the “no branch” option and any unauthorized branch for non-super
  admins (only assigned branches are listed).
- 1 test (130 total) for the day filter.

## 2026-10-24 — Security fix: branch scope enforced server-side

### Fixed
- `IsAdmin` (permission admin) was also bypassing branch scoping everywhere: branch lists, branch
  switching and data queries. Branch bypass is now reserved for `IsSuperAdmin` only.
- Branch options/list are filtered by the user's assigned branches; warehouse and partner scope
  filters use the same rule.
- Added `BranchScope.Ensure` guards on 11 write paths (bookings, POS checkout/appointment, treasury
  and vouchers, shifts, sales invoices, deliveries, appointments, settlements) so posting to a
  foreign branch returns 403 even if the UI is bypassed.
- 3 tests (129 total): branch options limited for a non-super-admin, super admin still sees all,
  and creating a document in a foreign branch is forbidden.

## 2026-10-24 — Appointment bookings without prices or quantities

### Added
- Migration `0035`: `bookings.starts_at`/`ends_at` with a positive-range check and an index.
- POS appointment mode: choose customer + service + date + from/to time; the booking is saved with
  quantity 1 and price 0 (no price, quantity, tax or collection), and no voucher/invoice is created.
- Booking list shows the appointment time; booking create/update persists the time range.
- 2 tests (126 total): appointment saved with time and zero amounts without vouchers, and rejection
  of non-service items or an invalid time range.

## 2026-10-24 — Fix: sales group in navigation + POS redefined as booking with optional collection

### Fixed
- `NavMenu` was missing the `group.sales` group, so every sales/POS screen was registered but
  invisible in the sidebar. The group is now rendered.

### Changed
- POS quick sale now creates **only a booking** (`source = 'pos'`, confirmed, reserves stock when
  the setting is on). It no longer creates a delivery note or an invoice.
- Collection is optional: zero or more payment lines, each with its own treasury and payment
  method, grouped per treasury into one receipt voucher with multiple lines; over-collection above
  the sale total is rejected. The old forced single-method receipt was removed.
- 3 POS tests updated (121 total): booking-only without payment, multi-method collection in one
  voucher, and rejection + rollback when the collected total exceeds the sale.

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
