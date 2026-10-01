#!/usr/bin/env bash
# Creates the credentials the Compose files need, with random values (ai/SECURITY.md §5.3).
#
#   bash scripts/init-secrets.sh dev                          writes ./.env             (docker-compose-dev.yml)
#   bash scripts/init-secrets.sh prod --admin-email a@b.com   writes ./secrets/*        (docker-compose-prod.yml)
#
# Existing files are never overwritten. Both targets are untracked (.gitignore). Requires openssl.
set -euo pipefail

mode="${1:-}"
admin_email=""
shift || true
while [ $# -gt 0 ]; do
    case "$1" in
        --admin-email) admin_email="${2:-}"; shift 2 ;;
        *) echo "unknown argument: $1" >&2; exit 2 ;;
    esac
done

random() { openssl rand -hex "${1:-24}"; }

write_secret() { # name value
    local path="secrets/$1"
    if [ -e "$path" ]; then
        echo "keep    $path (already exists)"
        return
    fi
    umask 077
    printf '%s' "$2" > "$path"
    # The application runs as a non-root user inside the container and must be able to read the mounted file;
    # the directory itself stays private to the host user.
    chmod 0444 "$path"
    echo "created $path"
}

case "$mode" in
    dev)
        if [ -e .env ]; then
            echo "keep    .env (already exists)"
            exit 0
        fi
        umask 077
        cat > .env <<EOF
POSTGRES_PASSWORD=$(random)
VALKEY_PASSWORD=$(random)
RABBITMQ_PASSWORD=$(random)
DEV_ACCOUNT_EMAIL=dev.account@example.com
DEV_ACCOUNT_PASSWORD=$(random 16)
EOF
        echo "created .env (development account: dev.account@example.com; the password is in .env)"
        ;;
    prod)
        if [ -z "$admin_email" ]; then
            echo "prod needs --admin-email <address> for the first administrator" >&2
            exit 2
        fi
        mkdir -p secrets
        chmod 0700 secrets
        pg="$(random)"; valkey="$(random)"; rabbit="$(random)"
        migrator="$(random)"; runtime="$(random)"; reporting="$(random)"
        write_secret postgres_password.txt "$pg"
        # Least privilege (ai/DATABASE.md §3.2): the migration job and the API never use the bootstrap role.
        write_secret postgres_migrator_password.txt "$migrator"
        write_secret postgres_runtime_password.txt "$runtime"
        write_secret postgres_readonly_password.txt "$reporting"
        write_secret valkey_password.txt "$valkey"
        # RabbitMQ 4.3 reads the default credentials from a configuration file (the *_FILE variables are deprecated).
        write_secret rabbitmq_credentials.conf "default_user = app"$'\n'"default_pass = $rabbit"
        write_secret grafana_admin_password.txt "$(random)"
        write_secret identity_token_hash_key.txt "$(openssl rand -base64 64 | tr -d '\n')"
        write_secret bootstrap_admin_email.txt "$admin_email"
        write_secret bootstrap_admin_password.txt "$(random 16)"
        write_secret conn_postgres.txt "Host=postgres;Port=5432;Database=ecommerce;Username=app_runtime;Password=$runtime"
        write_secret conn_postgres_migrator.txt "Host=postgres;Port=5432;Database=ecommerce;Username=app_migrator;Password=$migrator"
        write_secret conn_valkey.txt "valkey:6379,password=$valkey"
        write_secret conn_rabbitmq.txt "amqp://app:$rabbit@rabbitmq:5672"
        if [ ! -e secrets/jwt_es384_private_key.pem ]; then
            umask 077
            openssl ecparam -name secp384r1 -genkey -noout | openssl pkcs8 -topk8 -nocrypt -out secrets/jwt_es384_private_key.pem
            chmod 0444 secrets/jwt_es384_private_key.pem
            echo "created secrets/jwt_es384_private_key.pem"
        else
            echo "keep    secrets/jwt_es384_private_key.pem (already exists)"
        fi
        echo "Back these files up in your secrets manager; losing the JWT key signs every user out."
        ;;
    *)
        echo "usage: bash scripts/init-secrets.sh dev | prod --admin-email <address>" >&2
        exit 2
        ;;
esac
