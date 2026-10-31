#requires -Version 5.1
<#
  Provisions a local development database (no .env required).
  Order: open port -> Docker container -> local PostgreSQL service -> instructions.
  Usage: powershell -ExecutionPolicy Bypass -File scripts\dev-db.ps1
#>
param(
    [int]$Port = 5432,
    [string]$Password = "postgres",
    [string]$Container = "ubp-postgres",
    [string]$Database = "ubp",
    [string]$QaDatabase = "ubp_qa"
)

$ErrorActionPreference = "Stop"

function Test-Port {
    param([int]$p)
    try {
        return (Test-NetConnection -ComputerName 127.0.0.1 -Port $p -InformationLevel Quiet -WarningAction SilentlyContinue)
    } catch {
        $client = New-Object System.Net.Sockets.TcpClient
        try { $client.Connect("127.0.0.1", $p); return $client.Connected }
        catch { return $false }
        finally { $client.Dispose() }
    }
}

Write-Host "==> Checking port 127.0.0.1:$Port"
if (Test-Port -p $Port) {
    Write-Host "OK: something is already listening on port $Port." -ForegroundColor Green
    Write-Host "   Next: dotnet run --project src/Ubp.Web -- --migrate"
    exit 0
}

if (Get-Command docker -ErrorAction SilentlyContinue) {
    Write-Host "==> Docker found: preparing container $Container"
    $existing = (& docker ps -a --filter "name=^/$Container$" --format "{{.Names}}") 2>$null
    if ($existing -eq $Container) {
        & docker start $Container | Out-Null
    } else {
        & docker run -d --name $Container `
            -e POSTGRES_PASSWORD=$Password -e POSTGRES_DB=$Database `
            -p "127.0.0.1:${Port}:5432" postgres:17 | Out-Null
    }

    $ready = $false
    for ($i = 0; $i -lt 40; $i++) {
        Start-Sleep -Seconds 1
        if (Test-Port -p $Port) { $ready = $true; break }
    }
    if (-not $ready) {
        Write-Host "Container did not start. Check: docker logs $Container" -ForegroundColor Red
        exit 1
    }

    & docker exec $Container createdb -U postgres $QaDatabase 2>$null | Out-Null
    Write-Host "OK: postgres is running on 127.0.0.1:$Port (postgres/$Password)" -ForegroundColor Green
    Write-Host "   Next: dotnet run --project src/Ubp.Web -- --migrate"
    exit 0
}

$service = Get-Service -Name "postgresql*" -ErrorAction SilentlyContinue | Select-Object -First 1
if ($service) {
    Write-Host "==> No Docker, but a local PostgreSQL service exists: $($service.Name)"
    if ($service.Status -ne "Running") {
        Start-Service $service
        Write-Host "   Service started."
    }
    Write-Host "   Make sure the postgres password is: $Password"
    Write-Host "   Create the databases if missing:"
    Write-Host "     psql -h 127.0.0.1 -U postgres -c ""CREATE DATABASE $Database;"""
    Write-Host "     psql -h 127.0.0.1 -U postgres -c ""CREATE DATABASE $QaDatabase;"""
    exit 0
}

Write-Host "Neither Docker nor a local PostgreSQL service was found." -ForegroundColor Yellow
Write-Host "  A) Install Docker Desktop, then run this script again."
Write-Host "  B) Install PostgreSQL 17, create database $Database, then:"
Write-Host "     dotnet run --project src/Ubp.Web -- --migrate"
exit 1
