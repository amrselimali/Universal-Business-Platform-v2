#requires -Version 5.1
<#
  Releases the file locks that block a build:
    1) stops any running Ubp.Web host (dotnet run / VS debugger host),
    2) shuts down lingering MSBuild / Roslyn build servers.
  Usage: powershell -ExecutionPolicy Bypass -File scripts\stop-app.ps1
#>
param([switch]$Rebuild)

$ErrorActionPreference = "Continue"

Write-Host "==> Stopping Ubp.Web hosts"
$hosts = Get-Process -Name "Ubp.Web", "Ubp.Web.exe" -ErrorAction SilentlyContinue
if (-not $hosts) { Write-Host "    none running" }
foreach ($process in $hosts) {
    Write-Host "    stop PID $($process.Id) ($($process.ProcessName))"
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
}

Write-Host "==> Shutting down build servers"
& dotnet build-server shutdown | Out-Null

Start-Sleep -Milliseconds 500
$still = Get-Process -Name "Ubp.Web" -ErrorAction SilentlyContinue
if ($still) {
    Write-Host "Still running (close the debugger in Visual Studio or press Shift+F5):" -ForegroundColor Yellow
    $still | ForEach-Object { Write-Host "    PID $($_.Id)" }
    exit 1
}

Write-Host "Locks released." -ForegroundColor Green

if ($Rebuild) {
    Write-Host "==> dotnet build"
    & dotnet build UniversalBusinessPlatform.slnx
    exit $LASTEXITCODE
}
