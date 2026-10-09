# Decisions

## 1. Hash chain and persistence

**Choice.** Each evidence has its own chain. Every custody event stores an HMAC-SHA-256 over a canonical encoding of its fields, the previous event's MAC included. A `KeyId` names the key: `dev` locally, `k1` from Key Vault in Azure. The first event commits the content's SHA-256, length and media type, so the evidence bytes are covered too.
- **Canonical format (v2):** a version byte, then each field in a fixed order as a big-endian uint32 length and its bytes. The fields are evidence code, sequence, kind, time (UTC ticks), actor, from and to custodians, notes (NFC UTF-8), content hash, length and media type, key id and previous MAC. Null is the length `0xFFFFFFFF`. Database ids are left out, so a copy of the data verifies the same.
- **Stable order:** `Seq` is assigned inside the write transaction while the evidence row is locked, and is unique per evidence; verification reads in `Seq` order. The evidence row keeps the head MAC and event count, so a missing last event is detected.
- **Append-only:** the API's database user has `DENY UPDATE, DELETE` on `CustodyEvents`.
- **Verification** recomputes every MAC and link, then checks the content hash, the current custodian and the transfer rows against the signed events. It reports the first failure: sequence, event id and reason.

**Discarded.** A plain SHA-256 chain: anyone with write access to the database can recompute it after an edit. SQL Server Ledger: it verifies whole tables against stored digests and cannot name the first invalid event of one evidence.

**Cost.** A secret to protect and rotate; old keys stay in the ring under their id. The proof convinces those who trust the key holder, not a third party. Verification reads the whole chain, about 10 events per evidence.

**Change signal.** An auditor must verify without trusting the operator, or the key may have leaked. Then sign chain heads with an asymmetric key and anchor them in immutable Blob storage (designed, not built).

## 2. Pagination

**Choice.** Keyset pagination over `EvidenceInbox`, a projection updated in the same transaction as every write.
- **Order and indexes:** rows are ordered by `(LastEventAtUtc, EvidenceId)`, and each filter has a composite index ending in those two columns.
- **Cursor:** opaque. It carries the last row's position and a hash of the filters and sort, so it cannot continue a different listing (`400`).
- **Next page:** the query reads one extra row to know whether another page follows.

**Discarded.** `OFFSET … FETCH` with a total count. It reads every skipped row: 653 logical reads for the last of 800 pages, against 88 by keyset (see [overview](overview.md)). Rows also shift between pages while events arrive.

**Cost.** No page numbers or total count: the UI offers "Primera página" and "Página siguiente". One index per filter, and every write also updates the projection. Text search uses `LIKE` and scans.

**Change signal.** Users need to jump to a page or see totals; filters are combined often enough to need their own indexes; text search dominates inbox latency (move to full-text search); inbox p95 above 800 ms.

## 3. Concurrency and optimistic UI

**Choice.**
- **State machine:** one table (`TransferTransitions`). An Investigador requests; only the recipient Custodio accepts or rejects; a decided transfer is final; a filtered unique index allows one pending transfer per evidence.
- **Optimistic concurrency:** each transfer has a `rowversion`, sent as a strong `ETag`. Accept and reject require `If-Match` (`428` without it).
  - A stale version or an invalid transition answers `409` with the current state, who acted, when, and the current `ETag`.
  - A row lock serialises writes to one evidence inside the transaction only, never across the user's think time.
- **Idempotency:** every write (request, accept, reject) carries a client-generated UUIDv7 `Idempotency-Key`.
  - The key and a SHA-256 fingerprint of the request are stored on the transfer row in the same transaction, unique per user.
  - A retry answers with that transfer and `Idempotent-Replayed: true`. The same key with another body is `422`; a retry while the first is still running is `409`.
- **UI:** React Router fetchers.
  - The key stays in `sessionStorage` until the outcome is known, so a retry or a reload resends it.
  - A dashed "Enviando…" row shows at once. A `409` shows the server's state and an alert that takes focus.
  - An answer that never arrives, or a `5xx`, makes the page read itself again and offer a retry with the same key.

**Discarded.** `412` for a stale `If-Match`, as RFC 9110 suggests: the brief asks for `409`, and one status gives the UI one path. A generic idempotency store (a table or a cache of responses): written apart from the transfer, it can fail on its own, and a retry would then run twice.

**Cost.** Keys live on the transfer rows and are kept indefinitely, and each new write type needs its own key columns. The UI carries an intent store and reconciliation logic.

**Change signal.** More write types (move keys to one table written in the same transaction); `409`s in normal use, which would mean real contention; key storage growth (expire keys after a retention period).

## 4. Azure production architecture

For a small team: about 20 users and a few thousand evidences a year.

| Concern | Choice |
|---|---|
| Compute | App Service Linux B2, Always On, system-assigned managed identity |
| Database | Azure SQL S2 (DTU): never pauses, Entra-only authentication, firewall limited to the app's outbound IPs |
| Evidence storage | Blob Storage with version-level immutability (WORM); SQL keeps the hash and the pinned version. Not deployed: the demo keeps the bytes in SQL. |
| Secrets | Key Vault: the JWT signing key and the HMAC key `k1`, read through Key Vault references with the managed identity |
| Observability | Application Insights and Log Analytics through OpenTelemetry, with the custom metric `evidence.chain.verify.failures`. A `4xx` is not counted as a failure. |
| SPA | Vercel (or Static Web Apps Standard) |

- **Environments:** one azd environment per stage (dev, staging, prod), each with its own resource group, Key Vault, managed identity and database. Production goes in its own subscription once access and billing must be split. The deployed demo uses the same templates with `APP_SERVICE_SKU=B1` and `SQL_DATABASE_SKU=S1`.
- **First alert:** `evidence.chain.verify.failures` with `fixture = false` above 0, at Sev 1. Any broken chain outside the seeded tamper fixtures means data changed outside the API. Next would come the 5xx ratio and inbox p95.
- **Monthly cost** (East US 2, pay-as-you-go): B2 $24.82, S2 $73.61, SPA $9, a 5-location availability test $21.60 and two log alerts $3. Key Vault, Blob and Log Analytics stay within about $1. The total is **≈ $132**. The deployed demo (B1 and S1, Central US) costs ≈ $45.

**Discarded.** Serverless SQL: it pauses, and the first request after a pause fails while the database resumes. A hardened edge (Front Door Premium, Private Link, zone redundancy): ≈ $1,550 a month, justified once exposure must shrink.

**Cost.** S2 has under one vCore, so integrity sweeps compete with transfers, and DTU hides which resource saturates. There are no private endpoints.

**Change signal.** DTU above 80 % for 15 minutes (move to S3 or vCore); evidence must stay in one country; a security review requires private networking.
