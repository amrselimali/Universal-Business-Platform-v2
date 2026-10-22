# PROJECT STATE — Universal Business Platform v2

## 2026-10-22 — Phase 3 delivered (accounting core + general ledger)

- Migrations 0001–0023 on PostgreSQL 15; 52 tables.
- Phase 1: auth/sessions/permissions/branches/audit/database admin/QA.
- Phase 2: inventory master data, partners, unified fiscal-period engine, stock documents,
  movements/balances, transfers, stocktakes, period close checks.
- Phase 3: chart of accounts (materialized path), cost centers, journals, entry drafting and
  posting, monthly GL balances and party balances, opening balances (incl. CSV), stock
  auto-journaling, and 5 accounting reports (ledger, trial balance, party statement,
  cost-center analysis, stock vs GL).
- 72 automated tests pass on a real PostgreSQL instance (20 for phase 3); the opt-in
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
