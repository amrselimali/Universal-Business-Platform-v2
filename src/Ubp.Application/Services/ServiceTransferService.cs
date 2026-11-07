using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

/// <summary>
/// تحويل خدمات/جلسات بين العملاء من نقطة البيع: الإنشاء مسودة (بلا أثر)، والترحيل من المراجعة
/// ينقل الكميات والقيمة ويُنشئ القيد من حساب العميل المحوَّل إليه إلى حساب المحوَّل منه.
/// </summary>
public sealed class ServiceTransferService
{
    private readonly IServiceTransferRepository _repo;
    private readonly PermissionService _permissions;
    private readonly AuditService _audit;

    public ServiceTransferService(IServiceTransferRepository repo, PermissionService permissions,
        AuditService audit)
    {
        _repo = repo; _permissions = permissions; _audit = audit;
    }

    /// <summary>الخدمات المتبقية القابلة للتحويل للعميل المحوَّل منه.</summary>
    public async Task<IReadOnlyList<TransferableServiceRow>> ListAvailableAsync(ActorContext actor,
        Guid branchId, Guid customerId, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.PosTerminal, ActionCodes.Checkout,
            ct: ct);
        BranchScope.Ensure(actor, branchId);
        return await _repo.ListAvailableAsync(branchId, customerId, ct);
    }

    public async Task<ServiceTransfer> CreateAsync(ActorContext actor, ServiceTransferInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.PosTerminal, ActionCodes.Checkout,
            ct: ct);
        BranchScope.Ensure(actor, input.BranchId);
        var transfer = await _repo.CreateAsync(input, actor.UserId, ct);
        await _audit.WriteAsync(actor, "service_transfers", transfer.Id.ToString(), "INSERT", null,
            new
            {
                transfer.DocNo, input.FromCustomerId, input.ToCustomerId,
                transfer.TotalQuantity, transfer.TotalAmount
            }, ct);
        // يبقى التحويل مسودةً ظاهرةً في الوردية وكشوف الحسابات كحركة معلّقة، ويُعتمد ويُرحَّل
        // عند مراجعة الوردية (مع إمكانية تعديل الكمية قبل الترحيل).
        return transfer;
    }

    public async Task<IReadOnlyList<ServiceTransfer>> ListAsync(ActorContext actor, Guid branchId,
        int limit = 100, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.PosTerminal, ActionCodes.View, ct: ct);
        BranchScope.Ensure(actor, branchId);
        return await _repo.ListAsync(branchId, limit, ct);
    }

    public async Task<ServiceTransfer> GetAsync(ActorContext actor, Guid transferId,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.PosTerminal, ActionCodes.View, ct: ct);
        var transfer = await _repo.GetAsync(transferId, ct) ?? throw new NotFoundAppException();
        BranchScope.Ensure(actor, transfer.BranchId);
        return transfer;
    }

    /// <summary>
    /// ترحيل التحويل: يفعّله يوزر المراجعة صاحب صلاحية الاعتماد على الورديات ويُنشئ قيد
    /// الفرق بين حسابَي العميلين عندما يُرحَّل التحويل.
    /// </summary>
    public async Task<ServiceTransfer> PostAsync(ActorContext actor, Guid transferId,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.TreasuryShifts, ActionCodes.Approve,
            ct: ct);
        var transfer = await _repo.GetAsync(transferId, ct) ?? throw new NotFoundAppException();
        BranchScope.Ensure(actor, transfer.BranchId);
        var posted = await _repo.PostAsync(transferId, actor.UserId, ct);
        await _audit.WriteAsync(actor, "service_transfers", transferId.ToString(), "UPDATE",
            new { status = ServiceTransferStatuses.Draft },
            new { status = posted.Status, posted.JournalEntryId }, ct);
        return posted;
    }
}
