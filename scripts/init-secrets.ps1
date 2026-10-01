# Creates the credentials the Compose files need, with random values (ai/SECURITY.md §5.3).
#
#   pwsh scripts/init-secrets.ps1 -Mode dev                           writes ./.env        (docker-compose-dev.yml)
#   pwsh scripts/init-secrets.ps1 -Mode prod -AdminEmail a@b.com      writes ./secrets/*   (docker-compose-prod.yml)
#
# Existing files are never overwritten. Both targets are untracked (.gitignore).
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('dev', 'prod')][string]$Mode,
    [string]$AdminEmail
)

$ErrorActionPreference = 'Stop'

function New-RandomHex([int]$Bytes = 24) {
    [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes($Bytes)).ToLowerInvariant()
}

function Write-Secret([string]$Name, [string]$Value) {
    $path = Join-Path 'secrets' $Name
    if (Test-Path $path) { Write-Host "keep    $path (already exists)"; return }
    [System.IO.File]::WriteAllText((Join-Path (Get-Location) $path), $Value)
    Write-Host "created $path"
}

if ($Mode -eq 'dev') {
    if (Test-Path .env) { Write-Host 'keep    .env (already exists)'; return }
    @(
        "POSTGRES_PASSWORD=$(New-RandomHex)"
        "VALKEY_PASSWORD=$(New-RandomHex)"
        "RABBITMQ_PASSWORD=$(New-RandomHex)"
        'DEV_ACCOUNT_EMAIL=dev.account@example.com'
        "DEV_ACCOUNT_PASSWORD=$(New-RandomHex 16)"
    ) | Set-Content -Path .env -Encoding utf8NoBOM
    Write-Host 'created .env (development account: dev.account@example.com; the password is in .env)'
    return
}

if (-not $AdminEmail) { throw 'prod needs -AdminEmail <address> for the first administrator' }

New-Item -ItemType Directory -Force -Path secrets | Out-Null
$pg = New-RandomHex; $valkey = New-RandomHex; $rabbit = New-RandomHex
Write-Secret 'postgres_password.txt' $pg
Write-Secret 'valkey_password.txt' $valkey
Write-Secret 'rabbitmq_credentials.conf' "default_user = app`ndefault_pass = $rabbit"
Write-Secret 'grafana_admin_password.txt' (New-RandomHex)
Write-Secret 'identity_token_hash_key.txt' ([Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(64)))
Write-Secret 'bootstrap_admin_email.txt' $AdminEmail
Write-Secret 'bootstrap_admin_password.txt' (New-RandomHex 16)
Write-Secret 'conn_postgres.txt' "Host=postgres;Port=5432;Database=ecommerce;Username=app;Password=$pg"
Write-Secret 'conn_valkey.txt' "valkey:6379,password=$valkey"
Write-Secret 'conn_rabbitmq.txt' "amqp://app:$rabbit@rabbitmq:5672"

$pem = 'secrets/jwt_es384_private_key.pem'
if (Test-Path $pem) {
    Write-Host "keep    $pem (already exists)"
}
else {
    $key = [System.Security.Cryptography.ECDsa]::Create([System.Security.Cryptography.ECCurve]::NamedCurves.nistP384)
    [System.IO.File]::WriteAllText((Join-Path (Get-Location) $pem), $key.ExportPkcs8PrivateKeyPem())
    Write-Host "created $pem"
}
Write-Host 'Back these files up in your secrets manager; losing the JWT key signs every user out.'
