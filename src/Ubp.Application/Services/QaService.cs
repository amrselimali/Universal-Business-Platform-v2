using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Data;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

public sealed class QaMatrixRow
{
    public QaTestDefinition Definition { get; init; } = new();
    public string Status { get; set; } = "Untested";
    public DateTimeOffset? LastRunAt { get; set; }
}

public sealed partial class QaService
{
    public static readonly IReadOnlyList<QaTestDefinition> Registry = new List<QaTestDefinition>
    {
        new() { Code = "PERMISSIONS_DEFAULT_DENY", NameAr = "المنع الافتراضي للصلاحيات", NameEn = "Default deny", Category = "Security", Criticality = "critical" },
        new() { Code = "PERMISSIONS_ROLE_GRANT", NameAr = "منح الصلاحية من الدور", NameEn = "Role grant", Category = "Security", Criticality = "critical" },
        new() { Code = "PERMISSIONS_USER_DENY", NameAr = "تغلب المنع الفردي", NameEn = "User deny override", Category = "Security", Criticality = "critical" },
        new() { Code = "PERMISSIONS_ADMIN_BYPASS", NameAr = "تجاوز الأدمن", NameEn = "Admin bypass", Category = "Security", Criticality = "high" },
        new() { Code = "AR_NORMALIZATION", NameAr = "توحيد البحث العربي", NameEn = "Arabic normalization", Category = "Database", Criticality = "high" },
        new() { Code = "SETTINGS_SINGLETON", NameAr = "صف إعدادات واحد", NameEn = "Settings singleton", Category = "Database", Criticality = "medium" },
        new() { Code = "BASE_CURRENCY_SINGLE", NameAr = "عملة أساسية واحدة", NameEn = "Single base currency", Category = "Database", Criticality = "high" },
        new() { Code = "AUDIT_IMMUTABLE", NameAr = "سجل الأنشطة غير قابل للتعديل", NameEn = "Audit immutability", Category = "Security", Criticality = "critical" },
        new() { Code = "VERSION_CONCURRENCY", NameAr = "التزامن بالنُسخ", NameEn = "Version concurrency", Category = "Database", Criticality = "high" }
    };

    private readonly IQaRepository _repository;
    private readonly PermissionService _permissions;
    private readonly IDbConnectionFactory _qa;
    private readonly DatabaseOptions _options;
    private readonly ILogger<QaService> _logger;

    public QaService(IQaRepository repository, PermissionService permissions,
        QaConnectionFactory qa, DatabaseOptions options, ILogger<QaService> logger)
    {
        _repository = repository; _permissions = permissions; _qa = qa;
        _options = options; _logger = logger;
    }

    public async Task<IReadOnlyList<QaMatrixRow>> GetMatrixAsync(ActorContext actor,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.QaCenter, ActionCodes.View, ct: ct);
        await _repository.EnsureDefinitionsAsync(Registry, ct);
        var latest = (await _repository.GetLatestResultsAsync(ct))
            .ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
        return Registry.Select(d => new QaMatrixRow
        {
            Definition = d,
            Status = latest.TryGetValue(d.Code, out var r) ? r.Status : "Untested",
            LastRunAt = latest.TryGetValue(d.Code, out var r2) ? r2.At : null
        }).ToList();
    }

    public async Task<IReadOnlyList<QaRunSummary>> GetRunsAsync(ActorContext actor,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.QaCenter, ActionCodes.View, ct: ct);
        return await _repository.ListRunsAsync(20, ct);
    }

    public async Task<IReadOnlyList<QaTestResult>> GetResultsAsync(ActorContext actor, Guid runId,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.QaCenter, ActionCodes.View, ct: ct);
        return await _repository.GetResultsAsync(runId, ct);
    }
}
