# CHANGELOG

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
