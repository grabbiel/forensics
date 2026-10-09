# Overview

## Tests

### How to run them

```bash
dotnet test --solution backend/EvidenceChain.slnx
```

```bash
cd frontend && npm test
```

Database tests start SQL Server 2025 in a container (Testcontainers), so they need Docker. Without it they skip on a laptop and fail on CI, so a green check always includes them. CI runs both suites on every pull request; `Backend tests` and `Frontend tests` are required checks on `main`.

The API tests run against real SQL Server, with no fakes. The two journeys run as the least-privilege `evidence_app` login over databases seeded with the reference dataset (seed 42), as in `docker compose`.

### What the brief asks to test

| Requirement | Where | What it shows |
|---|---|---|
| Tampered chain | `ReviewEvidenceTests` | Through `GET …/chain/verify`, the intact fixture is valid. The event-tampered fixture is `MAC_MISMATCH` at seq 3, the content-tampered one `CONTENT_HASH_MISMATCH` at 1, and the custodian-tampered one `CUSTODY_PROJECTION_MISMATCH`, each at the right event id, recorded as `Invalid` and counted as a fixture failure. |
| | `ChainVerifierTests` | Altering any canonical field of event *k* is reported at *k*. Directly edited rows and signed histories the state machine forbids are also caught. |
| Idempotency | `TransferCustodyTests` | The same key and body sent 8 times in parallel make one transfer, and every answer is 201 with its id. The same key with another body is 422. A retry spelled differently still replays. Decisions replay too, even when retried in parallel. |
| Concurrency conflict | `TransferCustodyTests` | Two parallel accepts with one `If-Match` give one 200 and one 409 with `currentState.status = "Accepted"`, who acted, when, and the current ETag. Bursts of duplicates and competing decisions never fail and leave every chain valid. |
| Gap-free codes | `DailyIndexAllocatorTests` | 55 parallel registrations on one type and day, 5 of them abandoned after taking a number, commit exactly codes 1 to 50. |

The frontend items, a stale search response that must not replace the current filter and the 409 rollback, arrive with the transfer UI.

### Also covered

- **Domain:** every state × command × role combination of the transfer state machine; the overdue rule's boundaries and severity; the canonical encoding pinned by a golden MAC.
- **Data:** the seed (identical loads matching `manifest.reference.csv`, all-or-nothing reruns); schema rules enforced by SQL Server; the app login's column-level permissions.
- **API:** sign-in and rejected tokens (wrong key, expired, `alg: none`); role and recipient policies; problem details for every client-facing error; `Idempotency-Key` and `If-Match` rules; inbox paging and filters; the published `openapi.yaml` matching the running API.

### Not tested, and why

- **Automated accessibility audits.** No axe or Lighthouse run is wired into CI.
- **Azure-specific auth paths.** The managed-identity SQL connection and Key Vault references only exist in App Service. Locally and on CI the API uses a SQL login and published development keys, so these paths are only exercised by deploying.
- **Load.** There are no load or soak tests; the suite proves behaviour under contention, not throughput.
