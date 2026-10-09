# Evidence Chain

A small application for a forensic team. It records digital evidence, its custody history and transfers between custodians, and proves that the history was not altered. Built with .NET 10, SQL Server 2025, React 19 and TypeScript.

Documentation: [overview](docs/overview.md) · [decisions](docs/decisions.md) · [code map](docs/code-map.md) · [AI usage](docs/ai-usage.md) · [AI code review](docs/ai-code-review.md)

## Run

Requirements: Docker Engine or Docker Desktop with the `docker compose` plugin, at least 4 GB of memory for containers, and ports 8080, 8081 and 1433 free. On Apple Silicon, see [below](#apple-silicon).

```bash
docker compose up --build
```

| URL | What |
|---|---|
| http://localhost:8080 | The web app |
| http://localhost:8081 | The API; its contract is at `/openapi/v1.json` (or `.yaml`) and in [`openapi.yaml`](openapi.yaml) |

The first run builds the images, which takes a few minutes. The one-shot `migrate` service then applies the migrations, creates the API's least-privilege login, loads the dataset and exits with code 0. The data lives in a Docker volume: `docker compose down -v` deletes it, and the next `up` seeds again.

### Demo users

Sign-in needs no password. Choose a user on the sign-in page, or type a user name under "Otra persona del equipo".

| User name | Name | Role | Permissions |
|---|---|---|---|
| `investigador.demo` | Lucía Ferrer | Investigador | Request transfers |
| `custodio.demo` | Diego Salas | Custodio | Accept or reject the transfers addressed to this user |
| `supervisor.demo` | Elena Ruiz | Supervisor | Review only |

Every signed-in user can review evidence and verify chains. The other users are:
- Investigadores: `martin.ochoa`, `irene.calvo`;
- Custodios: `nuria.paredes`, `oscar.villalba`, `carmen.robles`, `hector.lozano`, `paula.benitez`;
- Supervisor: `tomas.herrera`.

### Development-only credentials

[`compose.yaml`](compose.yaml) and the API's `appsettings.Development.json` hold four development-only secrets:
- the SQL Server `sa` password;
- the app login's password;
- the JWT signing key;
- the integrity key `dev`.

They are public on purpose, so the stack runs without setup, and each carries a `DevOnly` or `DEV-ONLY` marker. Two guards keep them out of real environments:
- **API:** it refuses to start outside `Development` when its connection string, JWT key or integrity keys are development-only.
- **Seeder:** it refuses to sign an Azure SQL database with the dev key.

To use other passwords, copy `.env.example` to `.env` and edit it.

## Synthetic data

The seed is deterministic (seed 42): 1,000 evidence items, 10,000 custody events, 4,502 transfers and 11 users.

- **Codes:** the type, the UTC registration date and a four-digit number per type and day, allocated without gaps, e.g. `LOG202609110007`.
- **Files:**
  - `.log` network and firewall logs, 1–5 KB;
  - `.csv` financial exports, 1–5 KB;
  - `.eml` messages, 2–10 KB.
- **Custodians:** each item has an initial and a current custodian.
- **Dates:** the history ends at 00:00 UTC on the day of the seed, so that day only the overdue fixture is overdue. Other pending transfers pass the 48-hour deadline over the following days; reseed with `docker compose down -v && docker compose up` before a demo.
- **Fixtures:** their codes depend on the seed day. The run that loads the seed prints them; see them with `docker compose logs migrate`, or reseed to print them again.

| Fixture | What it shows |
|---|---|
| intact | A chain that verifies |
| overdue transfer | A transfer pending past the 48-hour deadline, flagged as an anomaly |
| fresh pending | A transfer waiting for `custodio.demo` to accept or reject |
| large email | The largest `.eml` |
| event tampered | An edited event: verification fails at event 3 (`MAC_MISMATCH`) |
| content tampered | Edited content: verification fails at event 1 (`CONTENT_HASH_MISMATCH`) |
| custodian tampered | A custodian changed without a transfer (`CUSTODY_PROJECTION_MISMATCH`) |
| accepted late | Three transfers accepted after the deadline |

- **Export the files:** `dotnet run --project backend/tools/EvidenceChain.Seeder -- generate --out ./evidence-files` writes the 1,000 files and their `manifest.csv`.
- **Reproducibility:** [`database/synthetic/manifest.reference.csv`](database/synthetic/manifest.reference.csv) lists every file of seed 42 anchored on 2026-10-01, with its code, size, SHA-256 and custodians. `generate --seed 42 --anchor 2026-10-01T00:00:00Z` reproduces it byte for byte, and a unit test checks this on every pull request.
- **200,000 events:** copy `.env.example` to `.env` and set `SEED_PROFILE=scale`. Then run `docker compose down -v && docker compose up --build` to load 20,000 items.

## Tests

```bash
(cd backend && dotnet test --solution EvidenceChain.slnx)   # .NET 10 SDK; Docker runs SQL Server for the database tests
(cd frontend && npm ci && npm test)                         # Node 24 (nvm use)
```

The backend command must run inside `backend/`, where `global.json` selects the test runner. Without Docker, the database tests are skipped locally; CI runs them and requires them to pass. The API tests use SQL Server 2025 in a container (Testcontainers), and the review and transfer tests connect as the least-privilege app login, as the compose stack does.

| Brief requirement | Tests | What they show |
|---|---|---|
| Tampered chain | `ReviewEvidenceTests`, `ChainVerifierTests` | Each tamper fixture fails at the right event with its own reason; editing any signed field of event *k* is reported at *k*. |
| Idempotency | `TransferCustodyTests` | Eight parallel sends of one key make one transfer; the same key with another body gets `422`. |
| Concurrency conflict | `TransferCustodyTests` | Two parallel accepts give one `200` and one `409` with the current state. |
| Stale response, 409 rollback | `frontend/src/journeys.test.tsx` | Through the real router and `fetch` against MSW: a late answer for an earlier filter never replaces the current one; a `409` never shows the transfer as accepted, shows the server's state and focuses an alert. |

Also covered:
- every state × command × role of the state machine;
- gap-free codes under 55 parallel registrations;
- the seed's reproducibility;
- problem details;
- the published contract.

What is not tested, and why, is in the [overview](docs/overview.md#not-tested-and-why).

## Development without app containers

Run each step in its own terminal:

```bash
docker compose run --rm migrate                      # starts SQL Server, applies migrations and the seed, then returns
dotnet run --project backend/src/EvidenceChain.Api   # API on http://localhost:5080
cd frontend && npm ci && npm run dev                 # http://localhost:5173, proxying /api to the API
```

## Apple Silicon

Microsoft publishes SQL Server images for amd64 only. Docker Desktop runs them with **Use Rosetta for x86_64/amd64 emulation on Apple Silicon** (Settings → General) turned on.

If that is not possible, point the stack at another SQL Server 2025 instance; Azure SQL follows [infra/README.md](infra/README.md) instead.
1. In `.env` (see [`.env.example`](.env.example)), set `ADMIN_CONNECTION` to a login that can create databases and logins.
2. Set `APP_CONNECTION` to `User Id=evidence_app` with the `APP_DB_PASSWORD` value.
3. Run `docker compose up --build --no-deps migrate api web`.

## Repository layout

| Path | Contents |
|---|---|
| `backend/` | .NET solution: API, application, domain and infrastructure layers; EF Core migrations in `src/EvidenceChain.Infrastructure/Persistence/Migrations`; the seeder; tests |
| `frontend/` | React SPA (Vite, React Router) |
| `database/` | Reference manifest and sample files of the synthetic data; [`measure-inbox.sql`](database/measure-inbox.sql), which needs [go-sqlcmd](https://learn.microsoft.com/sql/tools/sqlcmd/go-sqlcmd-utility) |
| `docs/` | The documents listed above |
| `infra/` | Bicep templates and the Azure runbook |
| `openapi.yaml` | The API contract; a test checks it against the running API |

## Azure deployment (optional)

`azd` and Bicep deploy the API to App Service, with Azure SQL, Key Vault and Application Insights; the SPA goes to Vercel. [`infra/README.md`](infra/README.md) lists the steps: provision, secrets, prepare the database, seed, deploy, and Vercel. `azd down --purge` removes everything. Running costs about $43 a month.
