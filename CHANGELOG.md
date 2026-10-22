# CHANGELOG

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
