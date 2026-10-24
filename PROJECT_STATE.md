# PROJECT STATE — Universal Business Platform v2

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

### Next
4F (settlements/commissions/aging/posting center) → 4G/4H → 6 → 7.
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
