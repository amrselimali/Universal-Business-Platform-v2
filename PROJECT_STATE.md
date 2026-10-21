# PROJECT STATE — Universal Business Platform v2

## 2026-10-21 — Phase 2 delivered (inventory + partners + periods)

- Migrations 0001–0017 on PostgreSQL 15; 43 tables.
- Phase 1 unchanged: auth/sessions/permissions/branches/audit/database admin/QA.
- Phase 2: units, item categories, items (multi-unit + barcodes), customers/suppliers
  (company or branch scope), unified fiscal-period engine (branch/module/company closures,
  checks, immutable events), stock documents (receipt/issue/transfer/adjustment/opening/reversal)
  with atomic posting, moving weighted average, negative-stock guard, partial transfer receiving,
  stocktakes with automatic adjustments, and stock balance/item-card reports.
- Screens added: `/inventory/units`, `/inventory/item-categories`, `/inventory/items`,
  `/partners/customers`, `/partners/suppliers`, `/inventory/documents`, `/inventory/stocktake`,
  `/accounting/periods`, `/reports/stock`.
- 46 automated tests pass on a real PostgreSQL instance; app boots and all screens return 200.

### Decisions
- Single company per database (no `tenant_id`) — documented in `docs/PHASE2-AR.md` (D1).
- Document numbers are branch-scoped and generated inside the posting transaction.
- Periods are checked inside the posting transaction; no posting into a closed scope.

### Known limitations
- No sales/purchase invoices, accounting journal, currency rates, serials/batches.
- Stocktake loads all active goods items of the warehouse (fine for mid-size warehouses).
- Item-card report uses filtered OFFSET pagination (revisit for millions of movements).
- QA center is still the foundation layer (no Playwright/coverage/gates in CI).
- No MFA or built-in rate limiting.

### Next
Sales/purchase invoices → accounting posting rules on the period engine → `tenant_id` + RLS
(if multi-company is approved) → advanced QA and partitioning.
