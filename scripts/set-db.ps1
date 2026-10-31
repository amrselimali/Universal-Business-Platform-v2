#requires -Version 5.1
<#
  يربط التطبيق بقاعدة Neon الموجودة مسبقاً عبر user-secrets (بلا أسرار في المستودع)،
  ثم يفحص الاتصال ويطبّق الترحيلات.

  الاستخدام:
    powershell -ExecutionPolicy Bypass -File scripts\set-db.ps1
    powershell -ExecutionPolicy Bypass -File scripts\set-db.ps1 -Source "postgresql://user:pass@ep-xxx.neon.tech/neondb?sslmode=require"

  ملاحظة: بلا -Source يقرأ الرابط من تاريخ git (commit 86960e4) ثم يحوّل الترحيل إلى العنوان المباشر.
#>
param(
    [string]$Source = "",
    [string]$AdminPassword = "ChangeMe!Neon2026",
    [switch]$NoMigrate
)

$ErrorActionPreference = "Stop"
Set-Location (Split-Path $PSScriptRoot -Parent)

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) { Write-Error "dotnet SDK غير موجود في PATH (ثبّت .NET 10 SDK)."; exit 1 }

if (-not $Source) {
    Write-Host "==> جلب الرابط القديم من تاريخ git (86960e4)"
    $json = git show 86960e4:src/Ubp.Web/appsettings.json | ConvertFrom-Json
    $Source = $json.Database.ConnectionString
}
if ([string]::IsNullOrWhiteSpace($Source)) {
    Write-Error "لم أجد رابط قاعدة. مرّر -Source `"postgresql://...`" (من لوحة Neon)."
    exit 1
}

# الترحيل يحتاج اتصالاً مباشراً: قفل pg_advisory_lock لا يعمل عبر transaction pooler
$direct = $Source -replace '-pooler', ''

Write-Host "==> حفظ الأسرار خارج المستودع (user-secrets)"
& dotnet user-secrets --project src/Ubp.Web set "Database:ConnectionString" $Source | Out-Null
& dotnet user-secrets --project src/Ubp.Web set "Database:MigrationConnectionString" $direct | Out-Null

$jwt = -join ((48..57) + (65..90) + (97..122) | Get-Random -Count 64 | ForEach-Object { [char]$_ })
& dotnet user-secrets --project src/Ubp.Web set "Jwt:Key" $jwt | Out-Null
& dotnet user-secrets --project src/Ubp.Web set "Bootstrap:SuperAdminPassword" $AdminPassword | Out-Null
Write-Host "    OK: تم حفظ 4 مفاتيح."

Write-Host "==> فحص الإعدادات والاتصال (بلا كشف أسرار)"
& dotnet run --project src/Ubp.Web -- --check-db
if ($LASTEXITCODE -ne 0) {
    Write-Host "فشل التحقق. راجع السطور أعلاه:" -ForegroundColor Yellow
    Write-Host "  • password authentication failed → كلمة المرور تغيّرت/خاطئة (استخدم رابطاً جديداً من Neon)."
    Write-Host "  • OPEN migrations=FAILED + pooler  → مرّر الرابط المباشر (بدون -pooler)."
    Write-Host "  • timeout / no such host           → الشبكة أو اسم المضيف."
    exit 1
}

if (-not $NoMigrate) {
    Write-Host "==> تطبيق الترحيلات"
    & dotnet run --project src/Ubp.Web -- --migrate
    if ($LASTEXITCODE -ne 0) { Write-Host "الترحيل فشل — أرسل سطر MIGRATIONS_FAILED." -ForegroundColor Yellow; exit 1 }
}

Write-Host ""
Write-Host "تم بنجاح ✔" -ForegroundColor Green
Write-Host "  التشغيل:  dotnet run --project src/Ubp.Web   →  http://localhost:5080"
Write-Host "  الدخول:   admin / (Bootstrap:SuperAdminPassword) ثم يُجبرك النظام على تغييرها"
Write-Host "  تنبيه:    دوّر كلمة مرور Neon من لوحة التحكم (القديمة مسرّبة في تاريخ git)"
