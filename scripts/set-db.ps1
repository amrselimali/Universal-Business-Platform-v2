#requires -Version 5.1
<#
  Connects the app to the existing Neon database through user-secrets
  (never storing secrets in the repository), then verifies the connection
  and applies migrations.

  Usage (from the repository root):
    powershell -ExecutionPolicy Bypass -File scripts\set-db.ps1
    powershell -ExecutionPolicy Bypass -File scripts\set-db.ps1 -Source "postgresql://user:pass@ep-xxx.neon.tech/neondb?sslmode=require"
    powershell -ExecutionPolicy Bypass -File scripts\set-db.ps1 -NoMigrate

  Without -Source it reads the previous connection string from git history
  (commit 86960e4) and uses the direct endpoint for migrations.
#>
param(
    [string]$Source = "",
    [string]$AdminPassword = "ChangeMe!Neon2026",
    [switch]$NoMigrate
)

$ErrorActionPreference = "Stop"
Set-Location (Split-Path $PSScriptRoot -Parent)

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "dotnet SDK was not found in PATH (.NET 10 SDK is required)."
    exit 1
}

if ([string]::IsNullOrWhiteSpace($Source)) {
    Write-Host "==> Reading the previous connection string from git history (86960e4)"
    try {
        $json = git show 86960e4:src/Ubp.Web/appsettings.json | ConvertFrom-Json
        $Source = $json.Database.ConnectionString
    } catch {
        $Source = ""
    }
}

if ([string]::IsNullOrWhiteSpace($Source)) {
    Write-Error "No connection string found. Pass -Source `"postgresql://...`" (copy it from the Neon console)."
    exit 1
}

# Migrations need the direct endpoint: pg_advisory_lock does not survive a transaction pooler.
$direct = $Source -replace '-pooler', ''

Write-Host "==> Storing secrets outside the repository (user-secrets)"
& dotnet user-secrets --project src/Ubp.Web set "Database:ConnectionString" $Source | Out-Null
& dotnet user-secrets --project src/Ubp.Web set "Database:MigrationConnectionString" $direct | Out-Null

$chars = (48..57) + (65..90) + (97..122)
$jwt = -join ($chars | Get-Random -Count 64 | ForEach-Object { [char]$_ })
& dotnet user-secrets --project src/Ubp.Web set "Jwt:Key" $jwt | Out-Null
& dotnet user-secrets --project src/Ubp.Web set "Bootstrap:SuperAdminPassword" $AdminPassword | Out-Null
Write-Host "    OK: 4 keys stored."

Write-Host "==> Checking configuration and connectivity (no secrets printed)"
& dotnet run --project src/Ubp.Web -- --check-db
if ($LASTEXITCODE -ne 0) {
    Write-Host "Verification failed. Common causes:" -ForegroundColor Yellow
    Write-Host "  - password authentication failed  -> the Neon password changed/wrong; use a fresh URL."
    Write-Host "  - OPEN migrations=FAILED + pooler -> pass the direct URL (without -pooler)."
    Write-Host "  - timeout / no such host          -> network or host name."
    exit 1
}

if (-not $NoMigrate) {
    Write-Host "==> Applying migrations"
    & dotnet run --project src/Ubp.Web -- --migrate
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Migration failed. Send the MIGRATIONS_FAILED line." -ForegroundColor Yellow
        exit 1
    }
}

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "  Run:     dotnet run --project src/Ubp.Web   ->  http://localhost:5080"
Write-Host "  Sign in: admin / Bootstrap:SuperAdminPassword (you will be forced to change it)"
Write-Host "  Warning: rotate the Neon password - the old one is exposed in git history."
