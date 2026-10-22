using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

public sealed class AccountingReportService
{
    private readonly IAccountingReportRepository _repository;
    private readonly PermissionService _permissions;

    public AccountingReportService(IAccountingReportRepository repository,
        PermissionService permissions)
    {
        _repository = repository; _permissions = permissions;
    }

    public async Task<TrialBalanceReport> TrialBalanceAsync(ActorContext actor, DateTime from,
        DateTime to, Guid? branchId, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.TrialBalanceReport, ActionCodes.View, ct: ct);
        if (to < from) throw new ValidationAppException("validation.date_range");
        return await _repository.TrialBalanceAsync(from.Date, to.Date, branchId, ct);
    }

    public async Task<LedgerPage> LedgerAsync(ActorContext actor, LedgerRequest request,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.LedgerReport, ActionCodes.View, ct: ct);
        if (request.AccountId == Guid.Empty)
            throw new ValidationAppException("validation.account_required");
        if (request.To < request.From) throw new ValidationAppException("validation.date_range");

        // الأستاذ شهري: الرصيد الافتتاحي وإجماليات الفترة من الأرصدة الشهرية المجمّعة،
        // والحركات تُعرض داخل حدود الشهر نفسه حتى تتطابق الأرقام مع البنود.
        request.From = new DateTime(request.From.Year, request.From.Month, 1);
        request.To = new DateTime(request.To.Year, request.To.Month,
            DateTime.DaysInMonth(request.To.Year, request.To.Month));
        return await _repository.LedgerAsync(request, ct);
    }

    public async Task<PartyStatementReport> PartyStatementAsync(ActorContext actor, char partyType,
        Guid partyId, DateTime from, DateTime to, Guid? branchId, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.PartyStatementReport, ActionCodes.View,
            ct: ct);
        if (partyId == Guid.Empty) throw new ValidationAppException("validation.party_required");
        if (to < from) throw new ValidationAppException("validation.date_range");
        return await _repository.PartyStatementAsync(partyType, partyId, from.Date, to.Date,
            branchId, ct);
    }

    public async Task<IReadOnlyList<CostCenterAnalysisRow>> CostCenterAsync(ActorContext actor,
        DateTime from, DateTime to, Guid? branchId, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.CostCenterReport, ActionCodes.View, ct: ct);
        if (to < from) throw new ValidationAppException("validation.date_range");
        return await _repository.CostCenterAsync(from.Date, to.Date, branchId, ct);
    }

    public async Task<IReadOnlyList<StockVsGlRow>> StockVsGlAsync(ActorContext actor,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.StockVsGlReport, ActionCodes.View, ct: ct);
        return await _repository.StockVsGlAsync(null, ct);
    }
}
