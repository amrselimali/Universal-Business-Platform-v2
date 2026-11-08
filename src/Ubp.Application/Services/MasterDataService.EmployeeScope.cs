using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;

namespace Ubp.Application.Services;

/// <summary>موظفو الفرع النشطون لقوائم الاختيار (مواعيد الموظفين/صرف السلفة) — 0093.</summary>
public sealed partial class MasterDataService
{
    /// <summary>
    /// كل الموظفين النشطين في الفرع (بلا شرط «منفّذ خدمة») لشاشات المواعيد وصرف السلفة — 0093.
    /// </summary>
    public async Task<IReadOnlyList<Employee>> EmployeeBranchOptionsAsync(ActorContext actor,
        Guid? branchId, CancellationToken ct = default)
    {
        var allowed = await _permissions.HasAsync(actor, ScreenCodes.Employees, ActionCodes.View,
                          ct)
                      || await _permissions.HasAsync(actor, ScreenCodes.SalesAppointments,
                          ActionCodes.View, ct)
                      || await _permissions.HasAsync(actor, ScreenCodes.SalesBookings,
                          ActionCodes.View, ct)
                      || await _permissions.HasAsync(actor, ScreenCodes.OperatingExpenses,
                          ActionCodes.View, ct)
                      || await _permissions.HasAsync(actor, ScreenCodes.PosTerminal,
                          ActionCodes.View, ct);
        if (!allowed) throw new ForbiddenException();

        if (branchId is { } id && id != Guid.Empty)
        {
            BranchScope.Ensure(actor, id);
            return await _employees.ListActiveOptionsAsync(new[] { id }, false, ct);
        }

        if (actor.IsSuperAdmin)
            return await _employees.ListActiveOptionsAsync(Array.Empty<Guid>(), true, ct);
        var scope = actor.CurrentBranchId is { } current
            ? new[] { current }
            : actor.AllowedBranchIds.ToArray();
        var allBranches = actor.AllowedBranchIds.Count == 0;
        return await _employees.ListActiveOptionsAsync(scope, allBranches, ct);
    }

}
