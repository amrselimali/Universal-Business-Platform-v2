using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

/// <summary>صرف سلفة لموظف من المصروفات التشغيلية على حساب مختار — 0084.</summary>
public sealed class EmployeeAdvanceService
{
    private readonly IEmployeeAdvanceRepository _repo;
    private readonly PermissionService _permissions;
    private readonly AuditService _audit;

    public EmployeeAdvanceService(IEmployeeAdvanceRepository repo, PermissionService permissions,
        AuditService audit)
    {
        _repo = repo; _permissions = permissions; _audit = audit;
    }

    public async Task<EmployeeAdvance> CreateAsync(ActorContext actor, EmployeeAdvanceInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.OperatingExpenses, ActionCodes.Create,
            ct: ct);
        BranchScope.Ensure(actor, input.BranchId);
        var advance = await _repo.CreateAndPostAsync(input, actor.UserId, ct);
        await _audit.WriteAsync(actor, "employee_advances", advance.Id.ToString(), "INSERT", null,
            new { advance.DocNo, advance.EmployeeId, advance.Amount, advance.ChargeAccountId }, ct);
        return advance;
    }

    public async Task<IReadOnlyList<EmployeeAdvance>> ListAsync(ActorContext actor, Guid branchId,
        DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.OperatingExpenses, ActionCodes.View,
            ct: ct);
        BranchScope.Ensure(actor, branchId);
        return await _repo.ListAsync(branchId, from, to, 200, ct);
    }
}
