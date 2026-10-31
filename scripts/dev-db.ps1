#requires -Version 5.1
<#
  تجهيز قاعدة تطوير محلية للـ Universal Business Platform بلا `.env`.
  يجرّب بالترتيب:
    1) إن كان المنفذ مفتوحاً بالفعل → يتوقف بنجاح.
    2) Docker متاح → يشغّل حاوية postgres وينشئ ubp/ubp_qa.
    3) خدمة PostgreSQL محلية → يشغّلها وينشئ القواعد الناقصة.
    4) وإلا → يطبع التعليمات.
  الاستخدام:  powershell -ExecutionPolicy Bypass -File scripts\dev-db.ps1
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
        try { $client.Connect("127.0.0.1", $p); return $client.Connected } catch { return $false } finally { $client.Dispose() }
    }
}

Write-Host "==> فحص المنفذ 127.0.0.1:$Port"
if (Test-Port -p $Port) {
    Write-Host "OK: هناك خدمة تستمع على المنفذ $Port بالفعل. لا حاجة لفعل شيء." -ForegroundColor Green
    Write-Host "   التالي: dotnet run --project src/Ubp.Web -- --migrate"
    exit 0
}

$docker = Get-Command docker -ErrorAction SilentlyContinue
if ($docker) {
    Write-Host "==> Docker موجود: تجهيز حاوية $Container"
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
        Write-Host "تعذّر تشغيل الحاوية. راجع: docker logs $Container" -ForegroundColor Red
        exit 1
    }

    & docker exec $Container createdb -U postgres $QaDatabase 2>$null | Out-Null
    Write-Host "OK: postgres يعمل على 127.0.0.1:$Port (المستخدم postgres / كلمة المرور $Password)" -ForegroundColor Green
    Write-Host "   قواعد جاهزة: $Database (+ $QaDatabase إن أُنشئت)"
    Write-Host "   التالي: dotnet run --project src/Ubp.Web -- --migrate"
    exit 0
}

$service = Get-Service -Name "postgresql*" -ErrorAction SilentlyContinue | Select-Object -First 1
if ($service) {
    Write-Host "==> لم أجد Docker، لكن هناك خدمة PostgreSQL محلية: $($service.Name)"
    if ($service.Status -ne "Running") {
        Start-Service $service
        Write-Host "   تم تشغيل الخدمة."
    }
    Write-Host "   تأكد أن كلمة مرور المستخدم postgres هي: $Password"
    Write-Host "   وأنشئ القواعد إن لم تكن موجودة:"
    Write-Host "     psql -h 127.0.0.1 -U postgres -c ""CREATE DATABASE $Database;"""
    Write-Host "     psql -h 127.0.0.1 -U postgres -c ""CREATE DATABASE $QaDatabase;"""
    exit 0
}

Write-Host "لا يوجد Docker ولا خدمة PostgreSQL على هذا الجهاز." -ForegroundColor Yellow
Write-Host "اختر أحد الخيارين:"
Write-Host "  A) ثبّت Docker Desktop ثم أعد تشغيل هذا السكربت."
Write-Host "  B) ثبّت PostgreSQL 17 (المُنصّب الرسمي) وأنشئ قاعدة $Database ثم:"
Write-Host "     dotnet run --project src/Ubp.Web -- --migrate"
exit 1
