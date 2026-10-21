using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;

namespace Ubp.Application.Services;

public sealed partial class PeriodService
{
    public async Task<IReadOnlyList<PeriodCheck>> RunChecksAsync(ActorContext actor, Guid id,
        string scopeType, Guid? branchId, string moduleCode, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.FinancialPeriods, ActionCodes.View, ct: ct);
        var period = await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();
        return await RunChecksAsync(actor, period, scopeType, branchId, moduleCode, true, ct);
    }

    public async Task<IReadOnlyList<PeriodCheck>> CloseAsync(ActorContext actor, Guid id,
        string scopeType, Guid? branchId, string moduleCode, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.FinancialPeriods, ActionCodes.ClosePeriod, ct: ct);
        var period = await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();
        if (period.Status == "CLOSED")
            throw new ValidationAppException("validation.period_already_closed");

        await ValidateScopeAsync(actor, scopeType, branchId, moduleCode, ct);
        var checks = await RunChecksAsync(actor, period, scopeType, branchId, moduleCode, true, ct);
        var blocked = checks.FirstOrDefault(c => c.Status == "BLOCKED");
        if (blocked is not null)
        {
            await _repository.AddEventAsync(id, scopeType, branchId, moduleCode, "CLOSE_REJECTED",
                actor.UserId, blocked.CheckCode, actor.TraceId, ct);
            throw new ValidationAppException("validation.period_close_blocked",
                new Dictionary<string, string> { ["check"] = blocked.CheckCode });
        }

        await _repository.UpsertClosureAsync(id, scopeType, branchId, moduleCode, true, actor.UserId, null, ct);
        await _repository.AddEventAsync(id, scopeType, branchId, moduleCode, "CLOSE",
            actor.UserId, null, actor.TraceId, ct);
        await _audit.WriteAsync(actor, "period_scope_closures", id.ToString(), "UPDATE", null,
            new { scopeType, branchId, moduleCode, status = "CLOSED" }, ct);
        return checks;
    }

    public async Task<IReadOnlyList<PeriodCheck>> FinalizeAsync(ActorContext actor, Guid id,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.FinancialPeriods, ActionCodes.ClosePeriod, ct: ct);
        var period = await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();
        if (period.Status == "CLOSED")
            throw new ValidationAppException("validation.period_already_closed");

        var checks = new List<PeriodCheck>();
        var openClosures = await _repository.CountOpenClosuresAsync(id, ct);
        checks.Add(Check(period, PeriodScopes.Company, null, PeriodModules.Inventory, "OPEN_CLOSURES",
            openClosures == 0 ? "PASS" : "BLOCKED",
            openClosures == 0 ? "كل النطاقات مغلقة" : $"عدد النطاقات المفتوحة: {openClosures}"));

        var drafts = await _repository.CountDraftDocumentsAsync(period.StartDate, period.EndDate, null, ct);
        checks.Add(Check(period, PeriodScopes.Company, null, PeriodModules.Inventory, "DRAFT_DOCUMENTS",
            drafts == 0 ? "PASS" : "BLOCKED", $"مسودات مخزون داخل الفترة: {drafts}"));

        var unbalanced = await _repository.CountUnbalancedBalancesAsync(ct);
        checks.Add(Check(period, PeriodScopes.Company, null, PeriodModules.Inventory, "UNBALANCED_STOCK",
            unbalanced == 0 ? "PASS" : "BLOCKED", $"أرصدة لا تطابق الحركات: {unbalanced}"));

        var transfers = await _repository.CountUnfinishedTransfersAsync(period.StartDate,
            period.EndDate, null, ct);
        checks.Add(Check(period, PeriodScopes.Company, null, PeriodModules.Inventory,
            "UNFINISHED_TRANSFERS", transfers == 0 ? "PASS" : "BLOCKED",
            $"تحويلات لم تُستكمل: {transfers}"));

        var stocktakes = await _repository.CountDraftStocktakesAsync(period.StartDate,
            period.EndDate, null, ct);
        checks.Add(Check(period, PeriodScopes.Company, null, PeriodModules.Inventory,
            "UNPOSTED_STOCKTAKES", stocktakes == 0 ? "PASS" : "BLOCKED",
            $"جلسات جرد لم تُرحّل: {stocktakes}"));

        var zeroCost = await _repository.CountZeroCostBalancesAsync(null, ct);
        checks.Add(Check(period, PeriodScopes.Company, null, PeriodModules.Inventory,
            "ZERO_COST_BALANCES", zeroCost == 0 ? "PASS" : "BLOCKED",
            $"أرصدة بتكلفة صفر: {zeroCost}"));

        foreach (var check in checks)
            await PersistAsync(check, actor.UserId, ct);

        var blocked = checks.FirstOrDefault(c => c.Status == "BLOCKED");
        if (blocked is not null)
        {
            await _repository.AddEventAsync(id, PeriodScopes.Company, null, PeriodModules.Inventory,
                "CLOSE_REJECTED", actor.UserId, blocked.CheckCode, actor.TraceId, ct);
            throw new ValidationAppException("validation.period_close_blocked",
                new Dictionary<string, string> { ["check"] = blocked.CheckCode });
        }

        await _repository.SetStatusAsync(id, "CLOSED", actor.UserId, null, ct);
        await _repository.AddEventAsync(id, PeriodScopes.Company, null, PeriodModules.Inventory,
            "COMPANY_FINAL_CLOSE", actor.UserId, null, actor.TraceId, ct);
        await _audit.WriteAsync(actor, "fiscal_periods", id.ToString(), "UPDATE",
            new { status = "OPEN" }, new { status = "CLOSED" }, ct);
        return checks;
    }

    public async Task ReopenAsync(ActorContext actor, Guid id, string scopeType, Guid? branchId,
        string moduleCode, string reason, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.FinancialPeriods, ActionCodes.ReopenPeriod, ct: ct);
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
            throw new ValidationAppException("validation.reopen_reason_required");

        var period = await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();
        if (period.Status == "CLOSED" && scopeType != PeriodScopes.Company)
            throw new ValidationAppException("validation.period_reopen_final_first");

        await ValidateScopeAsync(actor, scopeType, branchId, moduleCode, ct);
        if (scopeType == PeriodScopes.Company)
            await _repository.SetStatusAsync(id, "OPEN", actor.UserId, reason.Trim(), ct);

        await _repository.UpsertClosureAsync(id, scopeType, branchId, moduleCode, false,
            actor.UserId, reason.Trim(), ct);
        await _repository.AddEventAsync(id, scopeType, branchId, moduleCode, "REOPEN",
            actor.UserId, reason.Trim(), actor.TraceId, ct);
        await _audit.WriteAsync(actor, "period_scope_closures", id.ToString(), "UPDATE",
            new { status = "CLOSED" }, new { status = "OPEN", reason = reason.Trim() }, ct);
    }

    private async Task ValidateScopeAsync(ActorContext actor, string scopeType, Guid? branchId,
        string moduleCode, CancellationToken ct)
    {
        if (!PeriodModules.All.Contains(moduleCode, StringComparer.Ordinal))
            throw new ValidationAppException("validation.period_module_invalid");
        if (scopeType == PeriodScopes.Company)
        {
            if (branchId.HasValue)
                throw new ValidationAppException("validation.period_scope_invalid");
            return;
        }

        if (scopeType != PeriodScopes.Branch || !branchId.HasValue)
            throw new ValidationAppException("validation.period_scope_invalid");
        if (!actor.CanAccessBranch(branchId.Value))
            throw new ForbiddenException("error.branch_not_allowed");
        if (await _branches.GetAsync(branchId.Value, ct) is null)
            throw new ValidationAppException("validation.branch_required");
    }

    private async Task<List<PeriodCheck>> RunChecksAsync(ActorContext actor, FiscalPeriod period,
        string scopeType, Guid? branchId, string moduleCode, bool persist, CancellationToken ct)
    {
        var checks = new List<PeriodCheck>
        {
            Check(period, scopeType, branchId, moduleCode, "PERIOD_RANGE", "PASS",
                $"من {period.StartDate:yyyy-MM-dd} إلى {period.EndDate:yyyy-MM-dd}")
        };

        var drafts = await _repository.CountDraftDocumentsAsync(period.StartDate, period.EndDate,
            scopeType == PeriodScopes.Branch ? branchId : null, ct);
        checks.Add(Check(period, scopeType, branchId, moduleCode, "DRAFT_DOCUMENTS",
            drafts == 0 ? "PASS" : "BLOCKED", $"مسودات غير مرحّلة: {drafts}"));

        if (moduleCode == PeriodModules.Inventory)
        {
            var unbalanced = await _repository.CountUnbalancedBalancesAsync(ct);
            checks.Add(Check(period, scopeType, branchId, moduleCode, "UNBALANCED_STOCK",
                unbalanced == 0 ? "PASS" : "BLOCKED", $"أرصدة لا تطابق الحركات: {unbalanced}"));

            var transfers = await _repository.CountUnfinishedTransfersAsync(period.StartDate,
                period.EndDate, scopeType == PeriodScopes.Branch ? branchId : null, ct);
            checks.Add(Check(period, scopeType, branchId, moduleCode, "UNFINISHED_TRANSFERS",
                transfers == 0 ? "PASS" : "BLOCKED", $"تحويلات لم تُستكمل: {transfers}"));

            var stocktakes = await _repository.CountDraftStocktakesAsync(period.StartDate,
                period.EndDate, scopeType == PeriodScopes.Branch ? branchId : null, ct);
            checks.Add(Check(period, scopeType, branchId, moduleCode, "UNPOSTED_STOCKTAKES",
                stocktakes == 0 ? "PASS" : "BLOCKED", $"جلسات جرد لم تُرحّل: {stocktakes}"));

            var zeroCost = await _repository.CountZeroCostBalancesAsync(
                scopeType == PeriodScopes.Branch ? branchId : null, ct);
            checks.Add(Check(period, scopeType, branchId, moduleCode, "ZERO_COST_BALANCES",
                zeroCost == 0 ? "PASS" : "BLOCKED", $"أرصدة بتكلفة صفر: {zeroCost}"));
        }

        if (scopeType == PeriodScopes.Company)
        {
            var branches = await _repository.CountBranchesWithoutClosureAsync(period.Id, moduleCode, ct);
            checks.Add(Check(period, scopeType, branchId, moduleCode, "BRANCHES_WITHOUT_CLOSURE",
                branches == 0 ? "PASS" : "BLOCKED", $"فروع نشطة غير مغلقة: {branches}"));
        }

        if (persist)
            foreach (var check in checks)
                await PersistAsync(check, actor.UserId, ct);

        return checks;
    }

    private async Task PersistAsync(PeriodCheck check, Guid userId, CancellationToken ct) =>
        await _repository.AddCheckAsync(new PeriodCheckInput
        {
            PeriodId = check.PeriodId, ScopeType = check.ScopeType, BranchId = check.BranchId,
            ModuleCode = check.ModuleCode, CheckCode = check.CheckCode, Status = check.Status,
            Message = check.Message
        }, userId, ct);

    private static PeriodCheck Check(FiscalPeriod period, string scopeType, Guid? branchId,
        string moduleCode, string code, string status, string message) => new()
    {
        PeriodId = period.Id, ScopeType = scopeType, BranchId = branchId, ModuleCode = moduleCode,
        CheckCode = code, Status = status, Message = message, CheckedAt = DateTimeOffset.UtcNow
    };
}
