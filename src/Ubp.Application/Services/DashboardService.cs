using Ubp.Core.Models;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

/// <summary>لوحة المؤشرات: صلاحية الشاشة + نطاق فرع المستخدم، وقراءة من الإجماليات الجاهزة.</summary>
public sealed class DashboardService
{
    private readonly IDashboardRepository _repository;
    private readonly PermissionService _permissions;

    public DashboardService(IDashboardRepository repository, PermissionService permissions)
    {
        _repository = repository; _permissions = permissions;
    }

    public async Task<DashboardSnapshot> GetAsync(ActorContext actor, DateTime? now = null,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Dashboard, ActionCodes.View, ct: ct);
        var today = DateOnly.FromDateTime(now ?? DateTime.UtcNow);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        return await _repository.GetAsync(actor.CurrentBranchId, today, monthStart, ct);
    }
}
