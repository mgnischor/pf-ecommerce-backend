#!/usr/bin/env bash
# Builds one EF Core migrations bundle per bounded context (ai/DATABASE.md §6.1, ai/CONTAINERS.md §6.7).
#
#   bash scripts/build-migrations-bundles.sh [target-runtime] [output-directory]   (CONFIGURATION=Release by default)
#
# Each context owns its schema and its own migration history, so each has its own bundle:
# artifacts/migrations/migrate-identity, migrate-catalog, migrate-inventory. A bundle is a self-contained
# executable that needs no SDK; run it with scripts/apply-migrations.sh as the DDL-capable role (app_migrator).
# The contexts are discovered from the source tree, so a new bounded context cannot be forgotten.

set -euo pipefail

cd "$(dirname "$0")/.."

runtime="${1:-linux-x64}"
output="${2:-artifacts/migrations}"

dotnet tool restore >/dev/null
mkdir -p "$output"

contexts=()
for file in src/*/Infrastructure/Persistence/*DbContext.cs; do
    case "$file" in src/SharedKernel/*) continue ;; esac # the abstract base, not a context
    name="$(basename "$file" .cs)"
    contexts+=("$name")
done

if [ "${#contexts[@]}" -eq 0 ]; then
    echo "No DbContext found under src/*/Infrastructure/Persistence." >&2
    exit 1
fi

for context in "${contexts[@]}"; do
    bundle="migrate-$(echo "${context%DbContext}" | tr '[:upper:]' '[:lower:]')"
    echo "==> $context -> $output/$bundle"
    dotnet ef migrations bundle \
        --context "$context" \
        --project Portfolio.csproj \
        --self-contained \
        --target-runtime "$runtime"         --configuration "${CONFIGURATION:-Release}" \
        --output "$output/$bundle" \
        --force
done

echo "Bundles written to $output"
