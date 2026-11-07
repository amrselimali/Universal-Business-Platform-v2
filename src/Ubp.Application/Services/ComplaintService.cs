using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

/// <summary>موديول شكاوى بسيط: تسجيل شكوى على عميل وتحديد الموظف المتسبب وإغلاقها — 0086.</summary>
public sealed class ComplaintService
{
    private readonly IComplaintRepository _complaints;
    private readonly PermissionService _permissions;
    private readonly AuditService _audit;

    public ComplaintService(IComplaintRepository complaints, PermissionService permissions,
        AuditService audit)
    {
        _complaints = complaints; _permissions = permissions; _audit = audit;
    }

    public async Task<Guid> CreateAsync(ActorContext actor, ComplaintInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Complaints, ActionCodes.Create, ct: ct);
        BranchScope.Ensure(actor, input.BranchId);
        var id = await _complaints.CreateAsync(input, actor.UserId, ct);
        await _audit.WriteAsync(actor, "complaints", id.ToString(), "INSERT", null,
            new { input.BranchId, input.CustomerId, input.EmployeeId, input.Description }, ct);
        return id;
    }

    public async Task<IReadOnlyList<Complaint>> ListAsync(ActorContext actor, Guid branchId,
        DateTime? from, DateTime? to, string? status, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Complaints, ActionCodes.View, ct: ct);
        BranchScope.Ensure(actor, branchId);
        return await _complaints.ListAsync(branchId, from, to, status, 300, ct);
    }

    public async Task<bool> CloseAsync(ActorContext actor, Guid id, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Complaints, ActionCodes.Edit, ct: ct);
        var closed = await _complaints.CloseAsync(id, actor.UserId, ct);
        if (closed)
            await _audit.WriteAsync(actor, "complaints", id.ToString(), "UPDATE",
                new { status = ComplaintStatuses.Open }, new { status = ComplaintStatuses.Closed },
                ct);
        return closed;
    }
}
