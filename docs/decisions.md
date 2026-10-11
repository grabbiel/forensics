# Decisions

## 1. Hash chain and persistence

- **Choice.** One HMAC-SHA-256 chain per evidence item.
  - Each event's MAC covers its canonical encoding, including the previous event's MAC. `KeyId` names the key: `dev` locally, `k1` from Key Vault. The first event commits the content's SHA-256.
  - Encoding v2: a version byte, then each field in a fixed order as a big-endian length and its bytes (null `0xFFFFFFFF`, NFC UTF-8 text, UTC ticks). Row ids are excluded, so a copy verifies the same.
  - `Seq` is assigned under the evidence row's lock, unique per item; the row keeps the head MAC and event count, so truncation is detected. The API's database user cannot update or delete events.
- **Discarded.** A plain SHA-256 chain: anyone with write access can recompute it. SQL Server Ledger: it verifies whole tables and cannot name one item's first invalid event.
- **Cost.** The key is a secret to protect. Rotation adds a key under a new id, and the old key must stay for old events. The proof convinces only those who trust the key holder.
- **Change signal.** Third-party verification or a suspected key leak: sign chain heads with an asymmetric key and anchor them in immutable Blob storage.

## 2. Pagination

- **Choice.** Keyset pagination over `EvidenceInbox`, a projection updated in each write's transaction and ordered by `(LastEventAtUtc, EvidenceId)`.
  - Each filter (type, custodian, integrity) has a covering index ending in those columns.
  - The opaque cursor holds a row's position, a direction (pages go forward and back) and a hash of the filters and sort, so it cannot continue another listing (`400`).
- **Discarded.** `OFFSET` with a total count: it reads every skipped row (592 reads for page 800, 9 by keyset), and rows shift as events arrive.
- **Cost.** No page numbers or totals; a projection update on every write, which moves its row in four covering indexes (47 reads, not 19). Text search and combined filters scan.
- **Change signal.** Users need page numbers or totals; combined filters become common; text search dominates latency (move to full-text search); inbox p95 above 800 ms.

## 3. Concurrency and optimistic UI

- **Choice.**
  - **State machine:** `TransferTransitions.Decide`. An Investigador requests, only the recipient Custodio decides, and a decision is final. A filtered unique index allows one pending transfer per item.
  - **Optimistic concurrency:** accept and reject need `If-Match` with the transfer's `rowversion` ETag (`428` without it). A stale version or an invalid transition returns `409` with the current state, who acted, when, and the new ETag.
  - **Idempotency:** every write carries a UUIDv7 `Idempotency-Key`, stored with a request fingerprint on the transfer row in one transaction. A retry, even in parallel, gets the same transfer (once committed, without waiting for the lock); another body is `422`.
  - **UI:** React Router fetchers show "Enviando…" at once and keep the key in `sessionStorage` until the outcome is known. A `409` shows the server's state in a focused alert. A lost answer or a `5xx` reloads the evidence and offers a retry with the same key; after a `429`, which saved nothing, the retry waits for `Retry-After`.
- **Discarded.** `412` for a stale `If-Match`: the brief asks for `409`. A separate idempotency store: it can fail apart from the transfer and let a retry run twice.
- **Cost.** Keys stay forever; new write types need key columns; the UI keeps an intent store.
- **Change signal.** More write types (one key table, written in the same transaction); `409`s in normal use; key storage growth (expire old keys).

## 4. Azure production architecture

- **Choice.** For a small team (about 20 users, a few thousand items a year):
  - **Region:** Chile Central, 37 ms from the team; East US 2 is 129 ms (≈ $132 there).
  - **Compute:** App Service Linux B2, Always On, with a managed identity.
  - **Database:** Azure SQL S2 (DTU), never paused, Entra-only auth, firewalled to the app.
  - **Evidence storage:** Blob with version-level immutability (WORM); SQL keeps the hash and the pinned version. The demo keeps the bytes in SQL.
  - **Secrets:** the JWT and HMAC keys, in Key Vault, read through references.
  - **Observability:** App Insights (Log Analytics) via OpenTelemetry; a `4xx` is not a failure.
  - **Rate limits:** in the API, in memory. Token buckets per user and client address, a write budget shared by all users, and per-instance gates on searches and verifications; a `429` problem carries `Retry-After`. Addresses come from one trusted proxy hop.
  - **SPA:** Static Web Apps Standard; Vercel serves the demo.
  - **Environments:** one azd environment per stage, each with its own resource group, Key Vault, identity and database; production in its own subscription.
  - **First alert:** `evidence.chain.verify.failures` with `fixture = false` above 0, at Sev 1. A broken chain outside the fixtures means data changed outside the API.
  - **Cost:** **≈ $182 a month** (Chile Central, pay-as-you-go; Azure Retail Prices API, October 2026). B2 $34.75, S2 $103.05, Static Web Apps $9, a 5-location availability test $30.24, two log alerts $4.20; Key Vault, Blob and logs under $1. The Bicep provisions compute, SQL, Key Vault and monitoring; the rest is design only.
- **Discarded.** Serverless SQL (auto-pause delays or fails the first request after idle; turning it off removes the saving), Front Door Premium with Private Link (≈ $1,550 a month), and edge rate limiting (Front Door, API Management) for one instance.
- **Cost.** S2 has less than one vCore, so integrity sweeps compete with transfers, and DTU hides which resource saturates.
- **Change signal.** DTU above 80% for 15 minutes (move to S3 or vCore); data residency; a security review that requires private networking; a second instance (limits are per instance).
