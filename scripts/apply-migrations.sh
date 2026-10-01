#!/usr/bin/env bash
# Applies every migrations bundle, then re-applies the role grants (ai/DATABASE.md §6.1).
#
#   MIGRATOR_CONNECTION="Host=...;Database=ecommerce;Username=app_migrator;Password=..." \
#   PGOPTIONS="-c pf.migrator_password=... -c pf.runtime_password=... -c pf.readonly_password=..." \
#     bash scripts/apply-migrations.sh [bundle-directory]
#
# Order: provision roles and schemas -> run each bundle as app_migrator -> provision again, so the runtime and
# reporting roles are granted on the tables the migrations just created and denied on the migration history.
# Safe to re-run (at-least-once): a bundle with nothing to apply does nothing, and the provisioning is idempotent.
# The connection string is the secret: take it from the secret store, never from a file in the repository.

set -euo pipefail

cd "$(dirname "$0")/.."

bundles="${1:-artifacts/migrations}"
: "${MIGRATOR_CONNECTION:?set MIGRATOR_CONNECTION to the app_migrator connection string}"
: "${PGADMIN_CONNECTION:?set PGADMIN_CONNECTION to a libpq URI or key=value string of a role that may create roles}"

psql "$PGADMIN_CONNECTION" --set ON_ERROR_STOP=1 --file database/provision-roles.sql

for bundle in "$bundles"/migrate-*; do
    echo "==> $(basename "$bundle")"
    "$bundle" --connection "$MIGRATOR_CONNECTION"
done

psql "$PGADMIN_CONNECTION" --set ON_ERROR_STOP=1 --file database/provision-roles.sql
echo "Migrations applied."
