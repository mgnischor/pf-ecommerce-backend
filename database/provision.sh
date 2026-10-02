#!/bin/sh
# Provisions the roles and schemas, and later the grants (ai/DATABASE.md §3.2). Runs database/provision-roles.sql as the
# bootstrap role. The passwords come from secret files and travel as session settings (PGOPTIONS), so they never appear in
# the SQL, in a Compose or Kubernetes manifest, or on a command line.
#
# Where it runs decides the defaults:
#   Compose     PGHOST=postgres PGUSER=app PGDATABASE=ecommerce, secrets in /run/secrets (the defaults below).
#   Kubernetes  the manifest sets PGHOST, PGUSER (the managed instance admin), PGDATABASE and PGSSLMODE; the admin password
#               is the secret file postgres_password. psql reads PG* from the environment.
set -eu

secrets_dir="${SECRETS_DIR:-/run/secrets}"
secret() { cat "$secrets_dir/$1"; }

PGHOST="${PGHOST:-postgres}"
PGUSER="${PGUSER:-app}"
PGDATABASE="${PGDATABASE:-ecommerce}"
PGPASSWORD="$(secret postgres_password)"
PGOPTIONS="-c pf.migrator_password=$(secret postgres_migrator_password) -c pf.runtime_password=$(secret postgres_runtime_password) -c pf.readonly_password=$(secret postgres_readonly_password)"
export PGHOST PGUSER PGDATABASE PGPASSWORD PGOPTIONS

exec psql --set ON_ERROR_STOP=1 --file /provision/provision-roles.sql
