# Overview

Evidence Chain records digital evidence, its custody history and its transfers. Both journeys in the brief run end to end, from React to SQL Server.

**Done.** An inbox with filters in the URL and keyset pages. Evidence detail with the custody timeline, the overdue-transfer anomaly and chain verification. Transfers through a state machine, with `If-Match`, `409` and `Idempotency-Key`, shown as pending at once. An append-only HMAC chain, JWT roles, problem+json, a tested `openapi.yaml` and a seed of 1,000 items and 10,000 events. Extras: an Azure deployment, a 200,000-event measurement and an hourly integrity sweep.

**Left out.** File upload, rate limiting, i18n (the UI is in Spanish) and real identity: sign-in picks a demo user. The first alert is designed, not provisioned.

**Run, seed and test.** Run `docker compose up --build` and open http://localhost:8080; `migrate` applies the migrations and the seed. For tests, run `dotnet test --solution EvidenceChain.slnx` in `backend/` and `npm ci && npm test` in `frontend/`. The [README](../README.md) has details and the tests per requirement.

## How the main query was measured

[`database/measure-inbox.sql`](../database/measure-inbox.sql) replays the inbox SQL from EF Core and prints reads, time and plan. One page over 20,000 items (`SEED_PROFILE=scale`):

| Query | Reads | Plan |
|---|---|---|
| First page; one custodian | ≈ 10; ≈ 6 | Index scan or seek, no lookups |
| Last page by keyset | ≈ 9 | Index seek (cursor supplied) |
| Last page by `OFFSET` | 592 | Scan of all rows, 9 ms |
| Text search | 16 to 592 | Index scan, 8 to 54 ms |

Keyset cost stays flat with depth; `OFFSET` reads all rows. Covering indexes cost writes: an inbox update reads 47 pages, not 19. At 1,000 items no query reads over 33 pages or takes 3 ms.

## Not tested, and why

- **Accessibility audits:** none were run. Keyboard paths and focus are tested; contrast was checked by hand.
- **Azure-only paths:** the managed identity and Key Vault references are checked by hand after deploying.
- **Load and browsers:** contention is tested for correctness, not throughput. Tests run in jsdom, and the UI was checked by hand in a Chromium-based browser.
