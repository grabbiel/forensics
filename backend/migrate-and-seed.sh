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

echo "3/3 Loading data"
# Day 2 replaces 'tracer' with: seed --if-empty --seed 42
dotnet seeder/EvidenceChain.Seeder.dll tracer --connection "$ADMIN_CONNECTION"
