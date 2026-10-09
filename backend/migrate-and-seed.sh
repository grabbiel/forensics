#!/bin/sh
# One-shot job for docker compose: migrate, prepare, load data. Exits non-zero on any failure.
set -eu

: "${ADMIN_CONNECTION:?ADMIN_CONNECTION is required}"
: "${APP_DB_LOGIN:=evidence_app}"
: "${APP_DB_PASSWORD:?APP_DB_PASSWORD is required}"

echo "1/3 Applying migrations"
./efbundle --connection "$ADMIN_CONNECTION"

echo "2/3 Preparing database (RCSI, app login)"
dotnet seeder/EvidenceChain.Seeder.dll prepare --connection "$ADMIN_CONNECTION" \
  --app-login "$APP_DB_LOGIN" --app-password "$APP_DB_PASSWORD"

echo "3/3 Loading the synthetic dataset (skipped when this seed is already loaded)"
# The anchor defaults to today 00:00 UTC, so only the overdue fixture is overdue on any day.
dotnet seeder/EvidenceChain.Seeder.dll seed --if-empty --seed 42 --profile "${SEED_PROFILE:-reference}" \
  --connection "$ADMIN_CONNECTION"
