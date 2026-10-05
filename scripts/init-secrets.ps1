# Creates the credentials the Compose files need, with random values (ai/SECURITY.md §5.3).
#
#   pwsh scripts/init-secrets.ps1 -Mode dev                           writes ./.env        (docker-compose-dev.yml)
#   pwsh scripts/init-secrets.ps1 -Mode prod -AdminEmail a@b.com      writes ./secrets/*   (docker-compose-prod.yml)
#
# Existing files are never overwritten. Both targets are untracked (.gitignore).
#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('dev', 'prod')][string]$Mode,
    [string]$AdminEmail
)

$ErrorActionPreference = 'Stop'

# Both targets are paths relative to the repository root, wherever the script is started from.
Set-Location (Join-Path $PSScriptRoot '..')

function New-RandomHex([int]$Bytes = 24) {
    [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes($Bytes)).ToLowerInvariant()
}

# On Linux and macOS the default umask leaves new files readable by every local user: apply the same modes as
# init-secrets.sh (the container user must read secret files, so they stay world-readable inside a private directory).
function Set-Mode([string]$Path, [string]$Permissions) {
    if (-not $IsWindows) { chmod $Permissions $Path }
}

function Write-Secret([string]$Name, [string]$Value) {
    $path = Join-Path 'secrets' $Name
    if (Test-Path $path) { Write-Host "keep    $path (already exists)"; return }
    [System.IO.File]::WriteAllText((Join-Path (Get-Location) $path), $Value)
    Set-Mode $path 0444
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
    Set-Mode .env 0600
    Write-Host 'created .env (development account: dev.account@example.com; the password is in .env)'
    return
}

if (-not $AdminEmail) { throw 'prod needs -AdminEmail <address> for the first administrator' }

New-Item -ItemType Directory -Force -Path secrets | Out-Null
Set-Mode secrets 0700
$pg = New-RandomHex; $valkey = New-RandomHex; $rabbit = New-RandomHex
$migrator = New-RandomHex; $runtime = New-RandomHex; $reporting = New-RandomHex
Write-Secret 'postgres_password.txt' $pg
# Least privilege (ai/DATABASE.md §3.2): the migration job and the API never use the bootstrap role.
Write-Secret 'postgres_migrator_password.txt' $migrator
Write-Secret 'postgres_runtime_password.txt' $runtime
Write-Secret 'postgres_readonly_password.txt' $reporting
Write-Secret 'valkey_password.txt' $valkey
Write-Secret 'rabbitmq_credentials.conf' "default_user = app`ndefault_pass = $rabbit"
Write-Secret 'grafana_admin_password.txt' (New-RandomHex)
Write-Secret 'identity_token_hash_key.txt' ([Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(64)))
Write-Secret 'pagination_cursor_key.txt' ([Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(64)))
Write-Secret 'bootstrap_admin_email.txt' $AdminEmail
Write-Secret 'bootstrap_admin_password.txt' (New-RandomHex 16)
Write-Secret 'conn_postgres.txt' "Host=postgres;Port=5432;Database=ecommerce;Username=app_runtime;Password=$runtime"
Write-Secret 'conn_postgres_migrator.txt' "Host=postgres;Port=5432;Database=ecommerce;Username=app_migrator;Password=$migrator"
Write-Secret 'conn_valkey.txt' "valkey:6379,password=$valkey"
Write-Secret 'conn_rabbitmq.txt' "amqp://app:$rabbit@rabbitmq:5672"

$pem = 'secrets/jwt_es384_private_key.pem'
if (Test-Path $pem) {
    Write-Host "keep    $pem (already exists)"
}
else {
    # NamedCurves is a nested class: [ECCurve]::NamedCurves is $null in PowerShell, [ECCurve+NamedCurves] is the type.
    $key = [System.Security.Cryptography.ECDsa]::Create([System.Security.Cryptography.ECCurve+NamedCurves]::nistP384)
    try {
        [System.IO.File]::WriteAllText((Join-Path (Get-Location) $pem), $key.ExportPkcs8PrivateKeyPem())
    }
    finally {
        $key.Dispose()
    }
    Set-Mode $pem 0444
    Write-Host "created $pem"
}
Write-Host 'Back these files up in your secrets manager; losing the JWT key signs every user out.'
