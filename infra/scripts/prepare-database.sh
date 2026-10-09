#!/usr/bin/env bash
# Prepares the Azure SQL database after `azd provision` (roadmap §1.3), as the signed-in SQL Entra admin:
# migrate, create the API's managed-identity user (reads and verification verdicts; custody events never change),
# allow SNAPSHOT reads, set compatibility 170, insert tracer rows.
# Opens a firewall rule for this machine's public IP and always removes it on exit.
# Usage: infra/scripts/prepare-database.sh [--skip-tracer] [--ip <address>]
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
skip_tracer=false
ip=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --skip-tracer) skip_tracer=true; shift ;;
    --ip) ip="$2"; shift 2 ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
done

# azd outputs from `azd provision` (.azure/<env>/.env).
env_value() { azd env get-value "$1" 2>/dev/null; }
resource_group="$(env_value AZURE_RESOURCE_GROUP)"
sql_server="$(env_value SQL_SERVER_NAME)"
sql_fqdn="$(env_value SQL_SERVER_FQDN)"
database="$(env_value SQL_DATABASE_NAME)"
api_name="$(env_value SERVICE_API_NAME)"
api_principal_id="$(env_value API_PRINCIPAL_ID)"
for value in "$resource_group" "$sql_server" "$sql_fqdn" "$database" "$api_name" "$api_principal_id"; do
  [[ -n "$value" ]] || { echo "Missing azd outputs; run 'azd provision' first." >&2; exit 1; }
done

# Identifiers go into T-SQL, which can't parameterize them: accept only what our Bicep generates.
[[ "$api_name" =~ ^[a-z0-9-]+$ ]] || { echo "Unexpected web app name: $api_name" >&2; exit 1; }
[[ "$api_principal_id" =~ ^[0-9a-f-]{36}$ ]] || { echo "Unexpected principal id: $api_principal_id" >&2; exit 1; }

if [[ -z "$ip" ]]; then
  ip="$(curl -fsS https://api.ipify.org)"
fi
rule="laptop-$(date +%Y%m%d%H%M%S)"

echo "Opening SQL firewall for $ip ($rule)"
az sql server firewall-rule create -g "$resource_group" -s "$sql_server" -n "$rule" \
  --start-ip-address "$ip" --end-ip-address "$ip" -o none
trap 'echo "Removing firewall rule $rule"; az sql server firewall-rule delete -g "$resource_group" -s "$sql_server" -n "$rule" -o none' EXIT

# Your az/azd sign-in, through DefaultAzureCredential.
connection="Server=tcp:${sql_fqdn},1433;Database=${database};Authentication=Active Directory Default;Encrypt=True;"

echo "1/3 Applying migrations"
(cd "$repo_root/backend" && dotnet tool restore >/dev/null && dotnet ef database update \
  --project src/EvidenceChain.Infrastructure --startup-project src/EvidenceChain.Api --connection "$connection")

# FROM EXTERNAL PROVIDER needs an admin who can read the directory (a guest admin needs a directory role).
echo "2/3 Granting the API's managed identity its access"
sqlcmd -S "tcp:${sql_fqdn},1433" -d "$database" --authentication-method ActiveDirectoryDefault -b -Q "
IF DATABASE_PRINCIPAL_ID(N'${api_name}') IS NULL
    CREATE USER [${api_name}] FROM EXTERNAL PROVIDER WITH OBJECT_ID = '${api_principal_id}';
ALTER ROLE db_datareader ADD MEMBER [${api_name}];
-- Verification records its verdict, and only that, in the inbox projection.
GRANT UPDATE ON dbo.EvidenceInbox (IntegrityStatus, IntegrityCheckedAtUtc, IntegrityCheckedThroughSeq) TO [${api_name}];
-- Explicit DENY outlives any later write grant: custody history is never rewritten.
DENY UPDATE, DELETE ON dbo.CustodyEvents TO [${api_name}];
-- Multi-statement reads run in SNAPSHOT transactions; Azure SQL allows them by default.
IF (SELECT snapshot_isolation_state FROM sys.databases WHERE name = DB_NAME()) = 0
    ALTER DATABASE CURRENT SET ALLOW_SNAPSHOT_ISOLATION ON;
ALTER DATABASE CURRENT SET COMPATIBILITY_LEVEL = 170;"

if [[ "$skip_tracer" == false ]]; then
  echo "3/3 Inserting tracer rows (skipped when evidence exists)"
  dotnet run --project "$repo_root/backend/tools/EvidenceChain.Seeder" -- tracer --connection "$connection"
else
  echo "3/3 Tracer rows skipped"
fi
