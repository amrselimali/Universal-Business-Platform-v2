# PROJECT STATE — Universal Business Platform v2

## 2026-10-19 — Phase 1 delivered

- Standalone .NET 10 + PostgreSQL platform: `Ubp.Core` · `Ubp.Infrastructure` ·
  `Ubp.Application` · `Ubp.Web` (Blazor Server + MudBlazor 9) · `Ubp.Tests`.
- 21 tables, 10 DbUp migrations, Arabic normalization, version/audit/warehouse-consistency triggers.
- Cookie + JWT + server-side sessions + refresh-token rotation, account lockout, Default-Deny
  permissions with role grants, per-user overrides and data scope (own/branches/all).
- Screens: login, dashboard, settings (general/branches/warehouses/currencies/custom fields),
  users (branches/warehouses/roles/permissions/unlock/reset), roles, audit, QA, database admin.
- 25 automated tests pass; Playwright-verified desktop/mobile UI; QA suite 9/9 on isolated DB.
- Dockerfile, docker-compose, CI workflow, Arabic docs.

### Known limitations
- Single company per database (no `tenant_id`/RLS yet).
- `audit_log` not partitioned yet.
- QA center is the foundation layer (no Playwright/gates/coverage in CI yet).
- No MFA or built-in rate limiting.

### Next
`tenant_id` + RLS → first ERP slice (products → stock → sales invoice → journal) →
accounting posting rules → advanced QA.
