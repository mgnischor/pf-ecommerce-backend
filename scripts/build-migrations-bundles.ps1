<#
.SYNOPSIS
    Builds one EF Core migrations bundle per bounded context (ai/DATABASE.md §6.1, ai/CONTAINERS.md §6.7).

.DESCRIPTION
    Each context owns its schema and its own migration history, so each has its own bundle:
    artifacts/migrations/migrate-identity, migrate-catalog, migrate-inventory. A bundle is a self-contained executable
    that needs no SDK. The contexts are discovered from the source tree, so a new bounded context cannot be forgotten.

.EXAMPLE
    pwsh scripts/build-migrations-bundles.ps1 -TargetRuntime linux-x64
#>
[CmdletBinding()]
param(
    [string] $TargetRuntime = 'linux-x64',
    [string] $Output = 'artifacts/migrations',
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

dotnet tool restore | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'dotnet tool restore failed.' }
New-Item -ItemType Directory -Force $Output | Out-Null

$contexts = Get-ChildItem -Path 'src/*/Infrastructure/Persistence/*DbContext.cs' |
    Where-Object { $_.FullName -notmatch '[\\/]SharedKernel[\\/]' } | # the abstract base, not a context
    ForEach-Object { $_.BaseName }
if (-not $contexts) { throw 'No DbContext found under src/*/Infrastructure/Persistence.' }

foreach ($context in $contexts) {
    $bundle = 'migrate-' + ($context -replace 'DbContext$', '').ToLowerInvariant()
    Write-Host "==> $context -> $Output/$bundle"
    dotnet ef migrations bundle `
        --context $context `
        --project Portfolio.csproj `
        --self-contained `
        --target-runtime $TargetRuntime `
        --configuration $Configuration `
        --output (Join-Path $Output $bundle) `
        --force
    if ($LASTEXITCODE -ne 0) { throw "Bundle of $context failed." }
}

Write-Host "Bundles written to $Output"
