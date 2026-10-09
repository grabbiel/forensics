# Evidence Chain

A small application for a forensic team: it records digital evidence, its custody history and transfers between custodians, and proves that the history was not altered. It is built with .NET 10, SQL Server 2025, React 19 and TypeScript.

Documentation: [overview](docs/overview.md) · [decisions](docs/decisions.md) · [code map](docs/code-map.md) · [AI usage](docs/ai-usage.md) · [AI code review](docs/ai-code-review.md)

## Run

Requirements: Docker Desktop with Compose v2, at least 4 GB of memory for containers, and ports 8080, 8081 and 1433 free.

```bash
docker compose up --build
```

| URL | What |
|---|---|
| http://localhost:8080 | The web app |
| http://localhost:8081 | The API; its contract is at `/openapi/v1.json` (or `.yaml`) and in [`openapi.yaml`](openapi.yaml) |

The first run builds the images, which takes a few minutes. The one-shot `migrate` service then applies the migrations, creates the API's least-privilege login, loads the dataset and exits with code 0. The data stays in a Docker volume; `docker compose down -v` starts again from a fresh seed.

### Demo users

Sign-in needs no password: choose a user on the sign-in page, or type a user name under "Otra persona del equipo".

| User name | Name | Role | Can |
|---|---|---|---|
| `investigador.demo` | Lucía Ferrer | Investigador | Request transfers |
| `custodio.demo` | Diego Salas | Custodio | Accept or reject the transfers addressed to this user |
| `supervisor.demo` | Elena Ruiz | Supervisor | Review evidence and verify chains |

Every signed-in user can review evidence and verify chains. Other users: Investigadores `martin.ochoa` and `irene.calvo`; Custodios `nuria.paredes`, `oscar.villalba`, `carmen.robles`, `hector.lozano` and `paula.benitez`; Supervisor `tomas.herrera`.

### Development-only credentials

[`compose.yaml`](compose.yaml) and the API's `appsettings.Development.json` carry four development-only values: the SQL Server `sa` password, the app login's password, the JWT signing key and the integrity key `dev`. They are public on purpose, so the stack runs without setup, and each one carries a `DevOnly` or `DEV-ONLY` marker.
- The API refuses to start outside `Development` when any of them is configured.
- The seeder refuses to sign an Azure SQL database with the dev key.
- To use other passwords, copy `.env.example` to `.env` and edit it.

## Synthetic data

The seed is deterministic (seed 42): 1,000 evidences, 10,000 custody events, 4,502 transfers and 11 users.

- **Names:** `TYPE + YYYYMMDD + INDEX:D4`, for example `LOG202609110007`. That is the type, the UTC registration date, and the evidence's number for that type and day, allocated without gaps.
- **Files:**
  - `.log`: network and firewall logs, 1–5 KB;
  - `.csv`: financial exports, 1–5 KB;
  - `.eml`: messages, 2–10 KB.
- **Dates:** each evidence has an initial and a current custodian. The history ends at 00:00 UTC on the day the seed runs, so on any day only the overdue fixture is overdue.
- **Fixtures:** their codes depend on that day; `docker compose logs migrate` prints them.

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
- **Reproducibility:** [`database/synthetic/manifest.reference.csv`](database/synthetic/manifest.reference.csv) lists every file of seed 42 anchored on 2026-10-01: code, size, SHA-256 and custodians. `generate --seed 42 --anchor 2026-10-01T00:00:00Z` reproduces it byte for byte, and a unit test checks this on every pull request.
- **200,000 events:** set `SEED_PROFILE=scale` in `.env` before the first run, which loads 20,000 evidences.

## Tests

```bash
dotnet test --solution backend/EvidenceChain.slnx   # .NET 10 SDK; Docker runs SQL Server for the database tests
cd frontend && npm ci && npm test                   # Node 24 (nvm use)
```

Without Docker, the database tests are skipped locally; CI runs them and requires them to pass.

## Development without app containers

```bash
docker compose up -d mssql migrate                  # SQL Server and the seed only
dotnet run --project backend/src/EvidenceChain.Api  # API on http://localhost:5080
cd frontend && npm ci && npm run dev                # http://localhost:5173, proxying /api to the API
```

## Apple Silicon

Microsoft publishes SQL Server images for amd64 only. Docker Desktop runs them with **Use Rosetta for x86_64/amd64 emulation on Apple Silicon** (Settings → General) turned on.

If that is not possible, use any SQL Server 2025 or Azure SQL database instead of the container. Set `ADMIN_CONNECTION` and `APP_CONNECTION` in `.env` (see [`.env.example`](.env.example)), then run `docker compose up --build --no-deps migrate api web`.

## Repository layout

| Path | Contents |
|---|---|
| `backend/` | .NET solution: API, application, domain and infrastructure layers; EF Core migrations in `src/EvidenceChain.Infrastructure/Migrations`; the seeder; tests |
| `frontend/` | React SPA (Vite, React Router) |
| `database/` | Reference manifest and sample files of the synthetic data; [`measure-inbox.sql`](database/measure-inbox.sql) |
| `docs/` | The documents listed above |
| `infra/` | Bicep templates and the Azure runbook |
| `openapi.yaml` | The API contract; a test checks it against the running API |

## Azure deployment (optional)

`azd` and Bicep deploy the API to App Service, with Azure SQL, Key Vault and Application Insights; the SPA goes to Vercel. [`infra/README.md`](infra/README.md) lists the steps: provision, secrets, prepare the database, seed, deploy, and Vercel. `azd down --purge` removes everything. Running costs about $45 a month.
