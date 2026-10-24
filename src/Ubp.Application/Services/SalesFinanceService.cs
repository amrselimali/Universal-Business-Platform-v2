using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

/// <summary>تسويات الحجوزات والعمولات والأعمار ومركز الترحيل (4F).</summary>
public sealed class SalesFinanceService
{
    private readonly ISalesFinanceRepository _finance;
    private readonly ICommercialDocumentRepository _documents;
    private readonly PermissionService _permissions;
    private readonly AuditService _audit;

    public SalesFinanceService(ISalesFinanceRepository finance,
        ICommercialDocumentRepository documents, PermissionService permissions, AuditService audit)
    {
        _finance = finance; _documents = documents; _permissions = permissions; _audit = audit;
    }

    public async Task<IReadOnlyList<Settlement>> ListSettlementsAsync(ActorContext actor,
        Guid? branchId, string? status, int limit = 100, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.SalesSettlements, ActionCodes.View,
            ct: ct);
        return await _finance.ListSettlementsAsync(branchId, status, limit, ct);
    }

    public async Task<Settlement> GetSettlementAsync(ActorContext actor, Guid id,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.SalesSettlements, ActionCodes.View,
            ct: ct);
        return await _finance.GetSettlementAsync(id, ct) ?? throw new NotFoundAppException();
    }

    public async Task<Settlement> CreateSettlementAsync(ActorContext actor, SettlementInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.SalesSettlements, ActionCodes.Create,
            ct: ct);
        BranchScope.Ensure(actor, input.BranchId);
        var id = Guid.CreateVersion7();
        try { await _finance.CreateSettlementAsync(id, input, actor.UserId, ct); }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }

        var settlement = await _finance.GetSettlementAsync(id, ct)
                         ?? throw new NotFoundAppException();
        await _audit.WriteAsync(actor, "booking_settlements", id.ToString(), "INSERT", null,
            settlement, ct);
        return settlement;
    }

    public async Task<Settlement> ApproveSettlementAsync(ActorContext actor, Guid id,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.SalesSettlements, ActionCodes.Approve,
            ct: ct);
        Settlement settlement;
        try { settlement = await _finance.ApproveSettlementAsync(id, actor.UserId, ct); }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }

        await _audit.WriteAsync(actor, "booking_settlements", id.ToString(), "UPDATE",
            null, new { settlement.ApprovedBy, settlement.ApprovedAt }, ct);
        return settlement;
    }

    public async Task<Settlement> PostSettlementAsync(ActorContext actor, Guid id,
        CancellationToken ct = default)
    {
        var existing = await _finance.GetSettlementAsync(id, ct) ?? throw new NotFoundAppException();
        await _permissions.RequireAsync(actor, ScreenCodes.SalesSettlements, ActionCodes.Post,
            ct: ct);
        if (!existing.IsDraft)
            throw new ValidationAppException("validation.settlement_not_draft");

        Settlement settlement;
        try { settlement = await _finance.PostSettlementAsync(id, actor.UserId, ct); }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }

        await _audit.WriteAsync(actor, "booking_settlements", id.ToString(), "UPDATE",
            new { status = CommercialStatuses.Draft },
            new { settlement.Status, settlement.DocNo, settlement.JournalEntryId }, ct);
        return settlement;
    }

    public async Task<int> CalculateCommissionsAsync(ActorContext actor, CommissionInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.SalesCommissions,
            ActionCodes.Calculate, ct: ct);
        var count = await _finance.CalculateCommissionsAsync(input, actor.UserId, ct);
        await _audit.WriteAsync(actor, "employee_commissions", input.Month.ToString("yyyy-MM"),
            "INSERT", null, new { input.Month, input.Rate, input.Basis, count }, ct);
        return count;
    }

    public async Task<IReadOnlyList<CommissionRow>> ListCommissionsAsync(ActorContext actor,
        DateTime month, Guid? employeeId, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.SalesCommissions, ActionCodes.View,
            ct: ct);
        return await _finance.ListCommissionsAsync(month, employeeId, ct);
    }

    /// <summary>الأعمار الأربعة: عملاء، دفعات مقدمة، حجوزات، وموردون (المورد من 4B).</summary>
    public async Task<IReadOnlyList<AgingBucketRow>> AgingAsync(ActorContext actor, string kind,
        Guid? branchId, DateTime asOf, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.AgingReport, ActionCodes.View, ct: ct);
        return kind switch
        {
            "advances" => await _finance.AdvancesAgingAsync(branchId, asOf, ct),
            "bookings" => await _finance.BookingAgingAsync(branchId, asOf, ct),
            "suppliers" => (await _documents.SupplierAgingAsync(branchId, asOf, ct))
                .Select(r => new AgingBucketRow
                {
                    PartyId = r.SupplierId, PartyName = r.SupplierName, Current = r.Current,
                    Days30 = r.Days30, Days60 = r.Days60, Days90 = r.Days90, Over90 = r.Over90
                }).ToList(),
            _ => await _finance.CustomerAgingAsync(branchId, asOf, ct)
        };
    }

    /// <summary>أعمار بالفاتورة: تفصيل كل فاتورة مفتوحة بعمرها.</summary>
    public async Task<IReadOnlyList<AgingInvoiceRow>> AgingInvoicesAsync(ActorContext actor,
        Guid? branchId, DateTime asOf, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.AgingReport, ActionCodes.View, ct: ct);
        return await _finance.CustomerInvoiceAgingAsync(branchId, asOf, ct);
    }

    /// <summary>أعمار بالكمية: كميات الحجوزات غير المنفذة وعمرها.</summary>
    public async Task<IReadOnlyList<AgingQuantityRow>> AgingQuantitiesAsync(ActorContext actor,
        Guid? branchId, DateTime asOf, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.AgingReport, ActionCodes.View, ct: ct);
        return await _finance.BookingQuantityAgingAsync(branchId, asOf, ct);
    }

    public async Task<IReadOnlyList<UnpostedDocument>> ListUnpostedAsync(ActorContext actor,
        Guid? branchId, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.PostingCenter,
            ActionCodes.ViewUnposted, ct: ct);
        return await _finance.ListUnpostedAsync(branchId, ct);
    }

    public async Task<IReadOnlyList<ReconciliationCheck>> ReconcileAsync(ActorContext actor,
        Guid? branchId, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.PostingCenter, ActionCodes.Reconcile,
            ct: ct);
        return await _finance.ReconcileAsync(branchId, ct);
    }
}
