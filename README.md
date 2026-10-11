# Evidence Chain

Records digital evidence, its custody history and transfers between custodians, and proves the history was not altered. Built with .NET 10, SQL Server 2025, React 19 and TypeScript.

Docs: [overview](docs/overview.md) · [decisions](docs/decisions.md) · [code map](docs/code-map.md) · [AI usage](docs/ai-usage.md) · [AI code review](docs/ai-code-review.md)

## Run

Needs Docker with `docker compose`, 4 GB of memory for containers, and ports 8080, 8081 and 1433 free (see [Apple Silicon](#apple-silicon)).

**Windows:** Docker Desktop needs WSL 2 first: run `wsl --install` as administrator, restart, and pick the WSL 2 backend when installing Docker Desktop. Virtualization must be on in the BIOS/UEFI.

```bash
docker compose up --build
```

- **App:** http://localhost:8080
- **API:** http://localhost:8081, with its contract at `/openapi/v1.json` and in [`openapi.yaml`](openapi.yaml)

The one-shot `migrate` service applies the migrations, creates the API's least-privilege login and loads the seed. `docker compose down -v` deletes the data; the next `up` seeds again.

**Demo users.** No password: pick one, or type a user name under "Otra persona del equipo".

| User | Role | Can |
|---|---|---|
| `investigador.demo` | Investigador | Request transfers; is notified when they are decided |
| `custodio.demo` | Custodio | Accept or reject transfers sent to them; is notified of requests |
| `supervisor.demo` | Supervisor | Review; is notified of every transfer step |

Everyone can review evidence and verify chains. Other users: `martin.ochoa`, `irene.calvo` (Investigador); `nuria.paredes`, `oscar.villalba`, `carmen.robles`, `hector.lozano`, `paula.benitez` (Custodio); `tomas.herrera` (Supervisor).

**Development-only secrets.** The `sa` and app-login passwords, the JWT key and the integrity key `dev` are public in [`compose.yaml`](compose.yaml) and `appsettings.Development.json`, each marked `DevOnly` or `DEV-ONLY`. The API refuses to start with them outside `Development`, and the seeder refuses to sign Azure SQL with the dev key. To change the passwords, copy `.env.example` to `.env`.

## Synthetic data

Deterministic (seed 42): 1,000 items, 10,000 custody events and 11 users.

- **Codes:** the type, the UTC registration date and a gap-free four-digit number per type and day, e.g. `LOG202609110007`.
- **Files:** `.log` network and firewall logs and `.csv` financial exports, 1–5 KB; `.eml` messages, 2–10 KB.
- **Dates:** the history ends at 00:00 UTC on the seed day, when only the overdue fixture is overdue.
- **Fixtures**, with codes printed by `docker compose logs migrate`:
  - an intact chain;
  - an overdue transfer;
  - a pending transfer for `custodio.demo`;
  - the largest `.eml`;
  - three tampered chains: an edited event (`MAC_MISMATCH` at event 3), edited content (`CONTENT_HASH_MISMATCH` at event 1) and a custodian changed without a transfer (`CUSTODY_PROJECTION_MISMATCH`);
  - three transfers accepted late.
- **Export:** `dotnet run --project backend/tools/EvidenceChain.Seeder -- generate --out ./evidence-files` writes the files and their `manifest.csv`. With `--seed 42 --anchor 2026-10-01T00:00:00Z` it reproduces [`manifest.reference.csv`](database/synthetic/manifest.reference.csv) byte for byte, which a unit test checks.
- **Scale:** set `SEED_PROFILE=scale` in `.env`, then run `docker compose down -v && docker compose up --build` for 20,000 items and 200,000 events.

## Tests

```bash
(cd backend && dotnet test --solution EvidenceChain.slnx)   # .NET 10 SDK; Docker for the SQL Server tests
(cd frontend && npm ci && npm test)                         # Node 24
```

The database tests run SQL Server 2025 in a container (Testcontainers) and connect as the least-privilege app login. Without Docker they are skipped locally; CI requires them.

| Requirement | Tests | What they show |
|---|---|---|
| Tampered chain | `ReviewEvidenceTests`, `ChainVerifierTests` | Each tampered fixture fails at the right event, with its own reason |
| Idempotency | `TransferCustodyTests` | Eight parallel sends of one key make one transfer; another body with that key is `422` |
| Concurrency conflict | `TransferCustodyTests` | Two parallel accepts give one `200` and one `409` with the current state |
| Stale response, 409 rollback | `frontend/src/journeys.test.tsx` | A late answer never replaces the current filter; a `409` shows the server's state, never the transfer as accepted |

Also tested: every state × command × role of the state machine, gap-free codes under 55 parallel registrations, the seed's reproducibility, problem details and the published contract. What is not tested is in the [overview](docs/overview.md#not-tested-and-why).

## Development without app containers

```bash
docker compose run --rm migrate                      # SQL Server, migrations and seed
dotnet run --project backend/src/EvidenceChain.Api   # API on http://localhost:5080
cd frontend && npm ci && npm run dev                 # http://localhost:5173, proxying /api to the API
```

## Apple Silicon

SQL Server images are amd64 only, so turn on Docker Desktop's **Use Rosetta for x86_64/amd64 emulation on Apple Silicon** (Settings → General). Otherwise, use another SQL Server 2025:
1. In `.env`, set `ADMIN_CONNECTION` to a login that can create databases and logins.
2. Set `APP_CONNECTION` to `User Id=evidence_app` with the `APP_DB_PASSWORD` value.
3. Run `docker compose up --build --no-deps migrate api web`.

## Layout

- `backend/`: the .NET API, its layers, migrations, the seeder and tests.
- `frontend/`: the React SPA.
- `database/`: the synthetic data's reference manifest and samples, and [`measure-inbox.sql`](database/measure-inbox.sql) (needs [go-sqlcmd](https://learn.microsoft.com/sql/tools/sqlcmd/go-sqlcmd-utility)).
- `infra/`: Bicep and the Azure runbook.

## Azure (optional)

`azd` and Bicep deploy the API to App Service with Azure SQL, Key Vault and Application Insights; Vercel serves the SPA. [`infra/README.md`](infra/README.md) has the steps, and `azd down --purge` removes everything. The demo costs about $60 a month in Chile Central.
