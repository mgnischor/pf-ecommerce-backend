#!/usr/bin/env bash
# Applies every migrations bundle, then re-applies the role grants (ai/DATABASE.md §6.1).
#
#   PGADMIN_CONNECTION="postgresql://app@host:5432/ecommerce" PGPASSWORD=... \
#   MIGRATOR_CONNECTION="Host=...;Database=ecommerce;Username=app_migrator;Password=..." \
#   PGOPTIONS="-c pf.migrator_password=... -c pf.runtime_password=... -c pf.readonly_password=..." \
#     bash scripts/apply-migrations.sh [bundle-directory]
#
# PGADMIN_CONNECTION  libpq URI or key=value string of a role that may create roles (the bootstrap role). psql takes it
#                     as an argument, so leave the password out of it and pass it as PGPASSWORD.
# MIGRATOR_CONNECTION Npgsql connection string of app_migrator. It is handed to each bundle through a private temporary
#                     file (PF_DESIGN_TIME_CONNECTION_FILE), so the password never appears in the process list.
#
# Order: provision roles and schemas -> run each bundle as app_migrator -> provision again, so the runtime and
# reporting roles are granted on the tables the migrations just created and denied on the migration history.
# Safe to re-run (at-least-once): a bundle with nothing to apply does nothing, and the provisioning is idempotent.
# The connection strings are secrets: take them from the secret store, never from a file in the repository.

set -euo pipefail

cd "$(dirname "$0")/.."

bundles="${1:-artifacts/migrations}"
: "${MIGRATOR_CONNECTION:?set MIGRATOR_CONNECTION to the app_migrator connection string}"
: "${PGADMIN_CONNECTION:?set PGADMIN_CONNECTION to a libpq URI or key=value string of a role that may create roles}"

# Check before touching the database: with no bundle the first provisioning would run and the script would then fail.
shopt -s nullglob
bundle_files=("$bundles"/migrate-*)
if [ "${#bundle_files[@]}" -eq 0 ]; then
    echo "No migrations bundle in $bundles; build them with scripts/build-migrations-bundles.sh." >&2
    exit 1
fi

umask 077 # the connection file below is readable by this user only
connection_file="$(mktemp)"
trap 'rm -f "$connection_file"' EXIT
printf '%s' "$MIGRATOR_CONNECTION" > "$connection_file"
export PF_DESIGN_TIME_CONNECTION_FILE="$connection_file"

psql "$PGADMIN_CONNECTION" --set ON_ERROR_STOP=1 --file database/provision-roles.sql

for bundle in "${bundle_files[@]}"; do
    echo "==> $(basename "$bundle")"
    "$bundle"
done

psql "$PGADMIN_CONNECTION" --set ON_ERROR_STOP=1 --file database/provision-roles.sql
echo "Migrations applied."
