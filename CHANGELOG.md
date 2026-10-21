# CHANGELOG

## 2026-10-21 — Phase 2: inventory, partners, period engine

### Added
- Migrations `0012`–`0017`: document-sequence v2, units/items/barcodes, customers/suppliers
  with company-or-branch scope, unified period engine, stock documents/movements/balances,
  transfer receipts, and stocktakes.
- 21 tables, 9 screens (`/inventory/*`, `/partners/*`, `/accounting/periods`, `/reports/stock`).
- Atomic stock posting with moving weighted average, negative-stock guard, reversal documents,
  partial transfer receiving, stocktake adjustments and period-close checks/events.
- Permission actions `POST` · `REVERSE` · `RECEIVE` · `CLOSE_PERIOD` · `REOPEN_PERIOD`.
- 13 new automated tests (45 total) covering posting, costing, periods, transfers, stocktake.

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
