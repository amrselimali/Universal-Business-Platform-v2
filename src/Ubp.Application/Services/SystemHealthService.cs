using Ubp.Core.Models;
using Ubp.Core.Security;
using Ubp.Infrastructure.Migrations;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

/// <summary>
/// شاشة صحة النظام (7C): قاعدة البيانات، الترحيلات المعلّقة، الاستعلامات الطويلة،
/// وسجل النسخ الاحتياطي — كل ذلك للقراءة فقط وبصلاحية صريحة.
/// </summary>
public sealed class SystemHealthService
{
    private readonly ISystemHealthRepository _health;
    private readonly IBackupLogRepository _backups;
    private readonly MigrationRunner _runner;
    private readonly PermissionService _permissions;

    public SystemHealthService(ISystemHealthRepository health, IBackupLogRepository backups,
        MigrationRunner runner, PermissionService permissions)
    {
        _health = health; _backups = backups; _runner = runner; _permissions = permissions;
    }

    public async Task<SystemHealthSnapshot> GetAsync(ActorContext actor,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.SystemHealth, ActionCodes.View, ct: ct);

        var facts = await _health.GetFactsAsync(ct: ct);
        var applied = _runner.GetAppliedScripts().Select(a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pending = _runner.GetEmbeddedScriptNames()
            .Where(name => !applied.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        return new SystemHealthSnapshot(facts, applied.Count, pending.Count, pending);
    }

    public async Task<IReadOnlyList<BackupLogEntry>> BackupsAsync(ActorContext actor,
        int take = 20, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.SystemHealth, ActionCodes.View, ct: ct);
        return await _backups.ListAsync(take, ct);
    }
}
