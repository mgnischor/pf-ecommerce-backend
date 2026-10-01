#!/bin/sh
# Provisions the roles and schemas, and later the grants, from inside the Compose network (ai/DATABASE.md §3.2).
# Runs database/provision-roles.sql as the bootstrap role. The passwords come from secret files and travel as
# session settings (PGOPTIONS), so they never appear in the SQL, in the Compose file, or on a command line.
set -eu

secret() { cat "/run/secrets/$1"; }

PGPASSWORD="$(secret postgres_password)"
PGOPTIONS="-c pf.migrator_password=$(secret postgres_migrator_password) -c pf.runtime_password=$(secret postgres_runtime_password) -c pf.readonly_password=$(secret postgres_readonly_password)"
export PGPASSWORD PGOPTIONS

exec psql --host postgres --username app --dbname ecommerce --set ON_ERROR_STOP=1 --file /provision/provision-roles.sql
