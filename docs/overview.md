# Overview

Evidence Chain records digital evidence, its custody history and transfers between custodians. Both journeys in the brief run end to end, from React through the .NET 10 API to SQL Server.

## Scope

**Done**
- **Review evidence:** an inbox filtered by text, type, custodian and integrity, sorted by last event and paged by keyset, with every filter in the URL. The detail page shows the custody timeline, the overdue-transfer anomaly with severity and explanation, and on-demand chain verification that names the first invalid event.
- **Transfer custody:** request, accept and reject through an explicit state machine; `If-Match` with `409` carrying the current state; `Idempotency-Key` on every write. The UI shows the request as pending at once and reconciles it after a `409` or a lost answer.
- **Integrity:** an HMAC-SHA-256 chain per evidence. Custody events are append-only: the API's database user cannot update or delete them.
- **Platform:** JWT sign-in with server-side role and recipient checks; `application/problem+json` errors; `openapi.yaml` checked against the running API; a reproducible seed of 1,000 evidences and 10,000 events.
- **Extras:** deployment to Azure (azd + Bicep); a measurement with 200,000 events; an hourly integrity sweep; transfers accepted after the deadline are also flagged.

**Left out:** file upload (the seed provides the content), rate limiting and i18n (the UI is in Spanish). Sign-in picks a demo user without a password. The first alert is designed, not provisioned.

## Run, seed and test

`docker compose up --build`, then open http://localhost:8080; the API listens on http://localhost:8081. The one-shot `migrate` service applies the migrations and loads seed 42. The [README](../README.md) lists the demo users, fixtures and options.

Tests: `dotnet test --solution backend/EvidenceChain.slnx` (.NET 10 SDK, Docker) and `npm test` in `frontend/` (Node 24). Both are required checks on every pull request.

## How the main query was measured

[`database/measure-inbox.sql`](../database/measure-inbox.sql) runs the statements EF Core sends for `GET /api/v1/evidence` under `SET STATISTICS IO, TIME ON`. Logical reads per query:

| Query (26 rows) | 1,000 evidences, local / Azure SQL S1 | 20,000 evidences, local |
|---|---|---|
| First page | 35 / 40 | 89 (index seek on `IX_EvidenceInbox_Recent`, 26 key lookups) |
| First page for one custodian | 35 / 40 | 89 |
| Last page by keyset | 35 / 40 | 88 |
| Last page by `OFFSET` | 35 / 40 | 653 (clustered scan, sort of 20,001 rows; 23 ms) |
| Text search (`LIKE '%…%'`) | 35 / 40 | 653 (scan; 46 ms) |

At 1,000 rows the projection fits in 35–40 pages, so SQL Server scans it for every query, in under 3 ms. At 20,000 rows (`SEED_PROFILE=scale`), keyset reads stay flat at any depth while `OFFSET` reads the whole table. Text search is the next index to add.

## Tests

| Brief requirement | Where | What it shows |
|---|---|---|
| Tampered chain | `ReviewEvidenceTests`, `ChainVerifierTests` | Each tamper fixture fails at the right event with its own reason; editing any signed field of event *k* is reported at *k*. |
| Idempotency | `TransferCustodyTests` | Eight parallel sends of one key make one transfer; the same key with another body is `422`. |
| Concurrency conflict | `TransferCustodyTests` | Two parallel accepts give one `200` and one `409` with the current state. |
| Stale response, 409 rollback | `frontend/src/journeys.test.tsx` | Through the real router and `fetch` against MSW: an earlier filter's late answer never replaces the current one; a `409` never shows the transfer as accepted, shows the server's state and focuses an alert. |

The API tests run against SQL Server 2025 in a container (Testcontainers); the journey tests connect as the least-privilege app login. Also covered: every state × command × role of the state machine, gap-free evidence codes under 55 parallel registrations, the seed's reproducibility, problem details and the published contract.

**Not tested, and why**
- **Accessibility audits:** no axe or Lighthouse run. Keyboard paths and focus are tested; contrast was checked by hand.
- **Azure-only paths:** the managed-identity SQL connection and Key Vault references exist only in App Service; the deployment smoke test covers them.
- **Load:** contention is tested for correctness, not throughput.
- **Browsers:** components run in jsdom; the UI was checked by hand in Chromium only.
