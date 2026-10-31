# PROJECT STATE — Universal Business Platform v2

## 2026-10-31 — Phase 7 delivered → **all planned phases complete**

- `--migrate` deploy step, `backup_log` + `fn_record_backup` (external cron only),
  `/admin/system-health`, `/admin/import-tool` (preview writes nothing, execute is all-or-nothing with
  an `import_jobs` audit row), and login rate limiting (10/min/IP → 429).
- `docs/PHASE7-RUNBOOK-AR.md`: deploy order, health checks, backup/restore drill, rollback matrix and
  the pilot checklist.
- Migrations `0001`–`0045` (44 scripts, no gaps after `0019`), 229 tests passing, zero new packages,
  every file ≤ 300 lines.

### What remains is operational, not code
- Owner: rotate the Neon password, activate `.github/workflows/ci.yml`, branch protection, secrets,
  `ubp_app` role, and decide on Playwright (X-08/X-12).
- Pilot: run the checklist in the runbook against real data.

## 2026-10-31 — Phase 7 (part 1): operations surface

- `--migrate` CLI for CI/CD (`MIGRATIONS_OK/FAILED` + exit code); it is the recommended deploy step
  instead of `AutoMigrate=true`.
- Migration `0044`: `backup_log` + `fn_record_backup()` for the external cron; the app only reads.
- `/admin/system-health` (`SYSTEM_HEALTH` permission): database facts, long-running queries, applied
  and **pending** migrations, and the backup log.
- 224 tests pass (4 new). Live: `--migrate` applied the last migration and exited 0; the health page
  returned 200 with a recorded backup row.

### Remaining in Phase 7
- 7D import tool (`OpeningBalanceService.ImportCsvAsync` preview screen + `import_jobs`).
- 7E load & security pass (rate limiting on login, 1M-line and 100-user runs).
- 7F pilot run, user guides and rollback plan.
- Owner-side: rotate the Neon password, activate `.github/workflows/ci.yml`, branch protection,
  secrets, `ubp_app` role, Playwright decision (X-08/X-12).

## 2026-10-31 — Phase 6B delivered → **Phase 6 complete**

- Migration `0043`: `bi` schema with three materialized views (unique-indexed for concurrent
  refresh), `bi.fn_refresh_all()`, and the read-only `bi_reader` role with no `public` access.
- `BiRepository` + `BiRefreshWorker` (every `Bi:IntervalMinutes`, concurrent refresh).
- 220 tests pass (4 new). Live: the views matched the source after startup refresh and the role
  privileges were verified (`bi` = read-only, `public` = none).
- Phase 6 status: **6A ✅ 6B ✅ 6C ✅ 6D ✅ 6E ✅** (migrations `0039`–`0043`).

### Next: Phase 7 (operations & launch)
- 7A CI/CD migrations + direct Neon connection for `pg_advisory_xact_lock`/session locks.
- 7B backup job + `backup_log` reader screen; 7C health checks & slow-query log.
- 7D import tool (`OpeningBalanceService.ImportCsvAsync` + preview screen).
- 7E load/security pass; 7F pilot & rollback plan.
- Still owner-side: rotate the Neon password, activate `.github/workflows/ci.yml`, branch
  protection, secrets, `ubp_app` role, and the Playwright decision (X-08/X-12).

## 2026-10-31 — Phase 6E delivered (outbox + retry worker)

- Migration `0042`: `message_outbox` (dedupe unique index, due index, status/channel checks).
- `OutboxDispatcher` background service with lease-based claiming (`FOR UPDATE SKIP LOCKED`),
  exponential backoff (60s → 1h cap) and a terminal `FAILED` state; provider behind
  `IMessageSender` (currently a logging stub, X-15).
- 216 tests pass (12 new). Live: a queued WhatsApp message was delivered to the stub provider and
  marked `SENT` by the running worker; replaying the dedupe key added no row.
- Phase 6 status: 6A ✅ · 6C ✅ · 6D ✅ · 6E ✅ — remaining: 6B (`bi` schema + materialized views).

## 2026-10-31 — Phase 6C delivered (daily totals + dashboard)

- Migration `0041`: `sales_daily_totals` per `(day, branch)`; the row is written inside the invoice
  posting transaction (commits/rolls back with it) and returns subtract from the same aggregates.
- `/dashboard` (and `/`) show today/month sales, invoice count, gross margin, stock value and
  receivables, all read from aggregates — a measured read over 180 days × 3 branches is asserted to
  stay under 300 ms.
- 204 tests pass (3 new); build clean; every file ≤ 300 lines.

### Next in Phase 6
- 6E (`message_outbox` + hosted retry worker), then 6B (`bi` schema with materialized views and
  `bi_reader`), then Phase 7 (operations).

## 2026-10-31 — Phase 6D delivered (notifications)

- Migration `0040`: `notifications` (user XOR role target, severity/payload checks, unread indexes).
- `NotificationService` + `NotificationBroadcaster`: per-user fan-out to open circuits, unread
  counter, mark-read / mark-all / clear, and a trusted `SendAsync` other services can call.
- UI: bell with unread badge in the header and `/notifications/center` (subscribes and disposes).
- 201 tests pass (6 new); build clean; every file ≤ 300 lines.

### Next in Phase 6
- 6C (`sales_daily_totals` + dashboard under 300 ms), 6E (`message_outbox` + retry worker),
  6B (`bi` schema with materialized views and `bi_reader`).

## 2026-10-31 — Phase 6A delivered (reports center + exports)

- Migration `0039`: `saved_report_filters` (unique per user/report/name, JSON object only).
- `/reports/center` lists the approved `ReportCatalog` entries the user may open, manages per-user
  saved filters, and exports stock balances to CSV or XLSX (1000-row cap, X-13).
- XLSX is generated without any package (`ZipArchive` + minimal OOXML) — X-01 is now implemented,
  not just decided.
- 195 tests pass on a real PostgreSQL instance (16 new); build clean, every file ≤ 300 lines.

### Next in Phase 6
- 6B (`bi` schema + materialized views), 6C (`sales_daily_totals` dashboard), 6D (notifications +
  broadcaster), 6E (`message_outbox` + hosted retry worker).

## 2026-10-31 — Phase 3 gate and idempotency (0038)

- Migration `0038`: `stock_documents.idempotency_key` + partial unique index per branch, and
  `updated_by` on `stock_documents`/`stocktakes`. Next free number: `0039` (saved report filters).
- Posting/closing race is closed at the statement level: `period_scope_closures` and
  `fiscal_periods` close writes refuse to apply while an unposted stock document exists in the
  scope, so no document can be posted into a scope that its own closure already covered.
- Idempotent document creation (client retry safe) with audit-safe replay handling.
- 179 tests pass on a real PostgreSQL instance (14 new), including a 50-way concurrent posting run
  on an isolated database and a 5-round post-vs-close race.
- Known gap: the browser E2E scenario (Playwright) is still open — `docs/EXCEPTIONS.md` X-08.

### Next
- Phase 6 (`saved_report_filters`, reports center, dashboard, notifications) starting with `0039`.

## 2026-10-31 — Phases 1, 2.1 and 2.2 delivered

- Migrations `0036` (`users.must_change_password`, `password_changed_at`) and `0037`
  (`app_settings.time_zone`); the sequence stays gap-free after `0035` and no applied script was
  edited (G1). Next free numbers: `0038` idempotency, `0039` saved report filters.
- Forced password change at first sign-in, enforced in three places: session flag, HTTP gate
  (`PasswordChangeGate` → 302 for pages, 403 `auth.password_change_required` for APIs) and the Blazor
  layout (circuit navigation). Admin resets force the same flow.
- `IDisplayFormatter` is the single source for dates/times/numbers: reads `app_settings`
  (formats + IANA zone), converts UTC on display, and replaced all `.ToLocalTime()` usages and the
  hard-coded `Africa/Cairo` in the audit screen.
- `LookupService` (Application) replaced direct repository injection in 7 UI files; the schema DTOs
  moved to `Ubp.Core.Models`.
- Architecture tests now guard the layering and the 300-line cap (the CI gate has a test twin).
- 165 tests pass on a real PostgreSQL instance; build is clean (0 errors), every file ≤ 300 lines.

### Next
- Phase 3 concurrency gate (`IsolatedDatabaseFactory` + the eight scenarios), then `0038`
  idempotency keys for stock documents.

## 2026-10-31 — Phase 0 delivered (security, CI/CD, startup guards)

- The committed Neon credential (`neondb_owner` + password) was removed from
  `src/Ubp.Web/appsettings.json`; the file now holds no secrets. **The Neon password must still be
  rotated in the Neon console** — it remains in the git history.
- Startup configuration validation moved to `IValidateOptions<T>` + `ValidateOnStart()`
  (`src/Ubp.Web/Startup/Validation`) and runs before any database access; Production fails fast on
  missing/weak settings, an owner-role runtime connection, a development JWT key or a pooler
  migration endpoint.
- `BootstrapOptions` ships no default password and `DataSeeder` never creates the Super Admin
  without an explicit 12+ character password. Because the already-applied migration
  `0009_seed.sql` seeds `admin` with the well-known `Admin@123` hash, `DataSeeder` now detects that
  hash and replaces it with the configured `Bootstrap:SuperAdminPassword` (a Critical log is
  emitted when no strong password is configured). The migration itself is untouched.
- Health endpoints: `/health/live` (no database) and `/api/v1/system/health` →
  `{"status": ...}` only, `503` on failure, cached 15 s.
- CI now exists at `.github/workflows/ci.yml` (the old `ci/ci.yml` never ran) with the 300-line
  gate plus a committed-secrets/build-output guard.
- 141 automated tests (10 new) cover the startup guards and the removal of the default password;
  build is clean (0 errors) and every source file stays ≤ 300 lines.
- Documented decisions/exceptions: `docs/EXCEPTIONS.md` (X-01 … X-09), including deferring
  `must_change_password` to migration `0037` and XLSX export without `System.IO.Packaging`.

### Still open from Phase 0
- Rotate the Neon password and create/verify the dedicated `ubp_app` runtime role.
- Activate the pipeline: copy `docs/ci-workflow.yml` to `.github/workflows/ci.yml` (the automation
  token has no `workflows` permission), then enable branch protection on `main`.
- Move the server-side `pg_dump` backup job and the `backup_log` reader to Phase 7 (unchanged).
- Branch protection on `main` is a repository setting (not code) — enable it so merges require CI.

## 2026-10-24 — Phase 4C delivered (treasuries, vouchers, cashier shifts)

- Migrations `0001`–`0028`; phases 1, 2, 3, 4A-1, 4A-2, 4B and 4C are implemented on
  `cline/cdqzn9xp` (merged to `main` through PRs up to 4B).
- Phase 4C: treasuries + payment methods (clearing accounts, percent/fixed fees); multi-method
  receipts, payments and transfers with atomic GL posting; allocations against purchase invoices
  (advances → receivable/payable reclass); cashier shifts with one open shift per treasury,
  expected cash and over/short posting; three screens (`/treasury/management`,
  `/treasury/vouchers`, `/treasury/shifts`); 6 new tests.
- 96 automated tests pass on a real PostgreSQL instance; every source file ≤ 300 lines; zero
  NuGet packages added.

### Decisions
- Fees are computed from the payment method (percent + fixed) unless a voucher line overrides them.
- Vouchers are numbered at posting (`RCV/PAY/TRF-YYYY-NNNNNN`), shifts at opening (`SHIFT-…`).
- In-branch transfers post one entry; cross-branch transfers post a source entry, a destination
  entry and clear through `inter_branch_clearing`.
- Posted vouchers and invoices stay immutable; the single allowed exception is `allocated_total`
  on a posted invoice, guarded by a column-level comparison inside the trigger.
- Allocation posts a settlement entry (advances ↔ receivable/payable) so the party balance always
  equals the open invoice balance.
- Shift cash movements are derived from posted vouchers with cash methods between `opened_at`
  and closing time.

### Known limitations
- 4D onward not started; see `docs/PHASE-REMAINING-AR.md` for the full breakdown.
- Vouchers are not linked to a specific shift row; movements are time-window based.
- No cheque clearing dates, no multi-currency treasury, no bank statement import.

### Phase 4C-ب (completed 2026-10-24)
- `/treasury/reconciliation` + settlement entry, voucher cancel for drafts, shift reopen with
  reversal of the variance entry, and an allocation dialog on posted vouchers — 100 tests total.

### Phase 4E (completed 2026-10-24)
- Sales invoices from delivered booking lines with revenue/VAT posting and automatic advance
  consumption; sales returns with credit note and restock at the original cost; auto-invoice on
  delivery inside the delivery transaction; D3 and N24 guards; 2 screens; 5 tests (110 total).

### Phase 4F (completed 2026-10-24)
- Booking settlements (refund/forfeit posting, cancel releasing reservations, transfer), monthly
  commissions on three bases, four aging reports, and a posting center with three reconciliations;
  4 screens; 6 tests (116 total).

### Phase 4G (completed 2026-10-24)
- POS checkout in a single transaction: booking (source pos) + FEFO delivery + COGS + invoice +
  receipt voucher + allocation; rollback verified; 2 screens; 2 tests (118 total).

### Phase 4H (completed 2026-10-24)
- Customer follow-ups with one-time overdue generation; 1 screen; 2 tests (120 total).

### Fixes after review (2026-10-24)
- Added the missing `group.sales` group to the sidebar, so bookings/POS/invoices/returns/
  settlements/commissions/follow-ups are visible (employees were already under «التعريفات»).
- POS redefined per requirement: creates a booking only; collection is optional and can use
  multiple payment methods per operation.

### Next
6 (reports/notifications) → 7 (operations).
الخطة التفصيلية للباقي (جداول/شاشات/أزرار/دورات عمل/اختبارات/جهد): `docs/PHASE-REMAINING-AR.md`.

### Phase 4D (completed 2026-10-24)
- Bookings + reservations (`reserve_stock_on_booking`), appointments with DB overlap guard,
  delivery notes that issue FEFO stock, book COGS and complete the booking; 3 screens; 5 tests
  (105 total). Deferred: service-recipe consumption and auto-invoice on delivery (4E).

## 2026-10-22 — Phase 3 delivered (accounting core + general ledger)

- Migrations 0001–0023 on PostgreSQL 15; 52 tables.
- Phase 1: auth/sessions/permissions/branches/audit/database admin/QA.
- Phase 2: inventory master data, partners, unified fiscal-period engine, stock documents,
  movements/balances, transfers, stocktakes, period close checks.
- Phase 3: chart of accounts (materialized path), cost centers, journals, entry drafting and
  posting, monthly GL balances and party balances, opening balances (incl. CSV), stock
  auto-journaling, and 5 accounting reports (ledger, trial balance, party statement,
  cost-center analysis, stock vs GL).
- 75 automated tests pass on a real PostgreSQL instance (23 for phase 3); the opt-in
  performance test verifies trial balance and ledger on 1,000,000 journal lines.

### Decisions
- Single company per database (no `tenant_id`) — documented in `docs/PHASE2-AR.md` (D1).
- Document numbers are branch-scoped and generated inside the posting transaction.
- Periods are checked inside the posting transaction; no posting into a closed scope.
- GL reporting reads pre-aggregated monthly balances; entries update them atomically.
- Posted entries are immutable at the database level (triggers), reversals only.
- Permission catalog keys are `accounting.<screen>.<action>` while storage stays
  `SCREEN/ACTION` for backward compatibility.
- Stock journals use `item_category_accounts` for inventory/COGS and `posting_defaults` for
  clearing, opening equity, adjustment gain/loss and rounding.

### Known limitations
- Reports are monthly-grained for opening balances (GL is stored per month).
- No multi-currency revaluation, no tax/VAT posting, no sales/purchase invoices yet.
- Stock vs GL comparison is company-level (stock balances carry no branch).
- Opening-balance CSV import parses and applies in one session (no saved staging).
- QA center is still the foundation layer (no Playwright/coverage/gates in CI).
- No MFA or built-in rate limiting.

### Next
Sales/purchase invoices → accounting posting rules for invoices → cost allocation reports →
`tenant_id` + RLS (if multi-company is approved) → advanced QA and partitioning.
