#!/usr/bin/env bash
# Regenerates ../openapi.yaml from the API itself, so the published contract always matches the code.
# The OpenApiContractTests integration test fails when the committed file is stale.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
port="${OPENAPI_EXPORT_PORT:-5097}"
out="$here/src/EvidenceChain.Api/bin/openapi-export"

dotnet build "$here/src/EvidenceChain.Api" -o "$out" --nologo -v quiet >/dev/null

# The OpenAPI endpoint never touches SQL, so the development settings are enough.
(cd "$out" && ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="http://127.0.0.1:$port" exec dotnet EvidenceChain.Api.dll) >/dev/null 2>&1 &
api_pid=$!
trap 'kill "$api_pid" 2>/dev/null || true' EXIT

for _ in $(seq 1 60); do
  curl -fsS "http://127.0.0.1:$port/api/v1/health/live" >/dev/null 2>&1 && break
  sleep 1
done

curl -fsS "http://127.0.0.1:$port/openapi/v1.yaml" -o "$here/../openapi.yaml"
echo "Wrote openapi.yaml"
