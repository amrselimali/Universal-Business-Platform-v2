using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;

namespace Ubp.Application.Services;

public sealed partial class JournalEntryService
{
    /// <summary>توزيع سطر على مراكز تكلفة بنسب مئوية، وفرق التقريب على السطر الأخير.</summary>
    public async Task DistributeCostAsync(ActorContext actor, IList<JournalEntryLineInput> lines,
        int lineIndex, IReadOnlyList<CostDistributionLine> distributions, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.JournalEntry, ActionCodes.DistributeCost,
            ct: ct);

        if (lineIndex < 0 || lineIndex >= lines.Count) throw new NotFoundAppException();
        if (distributions.Count == 0) throw new ValidationAppException("validation.distribution_empty");
        var sum = distributions.Sum(d => d.Percentage);
        if (Math.Abs(sum - 100m) > 0.01m)
            throw new ValidationAppException("validation.distribution_total");

        var original = lines[lineIndex];
        var total = original.Debit > 0 ? original.Debit : original.Credit;
        if (total <= 0) throw new ValidationAppException("validation.line_amount");

        var allocated = 0m;
        var replacement = new List<JournalEntryLineInput>();
        for (var i = 0; i < distributions.Count; i++)
        {
            var share = i == distributions.Count - 1
                ? total - allocated
                : Math.Round(total * distributions[i].Percentage / 100m, 2, MidpointRounding.AwayFromZero);
            allocated += share;

            replacement.Add(new JournalEntryLineInput
            {
                AccountId = original.AccountId,
                Description = original.Description,
                CustomerId = original.CustomerId,
                SupplierId = original.SupplierId,
                CostCenterId = distributions[i].CostCenterId,
                Debit = original.Debit > 0 ? share : 0,
                Credit = original.Credit > 0 ? share : 0
            });
        }

        lines.RemoveAt(lineIndex);
        foreach (var line in replacement) lines.Insert(lineIndex++, line);
    }

    /// <summary>موازنة القيد تلقائياً: الفرق يُسجّل على الحساب المختار أو أكبر سطر.</summary>
    public async Task AutoBalanceAsync(ActorContext actor, IList<JournalEntryLineInput> lines,
        Guid? accountId, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.JournalEntry, ActionCodes.AutoBalance, ct: ct);
        if (lines.Count == 0) return;

        var difference = lines.Sum(l => l.Debit) - lines.Sum(l => l.Credit);
        if (difference == 0) return;

        var target = accountId
            ?? (difference > 0
                ? lines.OrderByDescending(l => l.Debit).First().AccountId
                : lines.OrderByDescending(l => l.Credit).First().AccountId);

        lines.Add(new JournalEntryLineInput
        {
            AccountId = target,
            Description = "موازنة تلقائية",
            Debit = difference < 0 ? -difference : 0,
            Credit = difference > 0 ? difference : 0
        });
    }

    internal async Task ValidateAsync(JournalEntryInput input, CancellationToken ct)
    {
        var journal = await _journals.GetAsync(input.JournalId, ct)
            ?? throw new NotFoundAppException("validation.journal_not_found");
        if (!journal.IsActive) throw new ValidationAppException("validation.journal_inactive");
        if (!journal.AllowManual)
            throw new ValidationAppException("validation.journal_manual_blocked");
        if (input.BranchId == Guid.Empty)
            throw new ValidationAppException("validation.branch_required");
        if (input.Lines.Count < 2)
            throw new ValidationAppException("validation.entry_needs_two_lines");
        if (input.Lines.Count > 200)
            throw new ValidationAppException("validation.entry_too_many_lines");

        var accounts = (await _accounts.ListPostableAsync(ct)).ToDictionary(a => a.Id);
        for (var i = 0; i < input.Lines.Count; i++)
        {
            var line = input.Lines[i];
            var number = (i + 1).ToString();

            if (!accounts.TryGetValue(line.AccountId, out var account))
                throw LineError("validation.account_not_postable", number);

            line.Debit = Math.Round(line.Debit, 2, MidpointRounding.AwayFromZero);
            line.Credit = Math.Round(line.Credit, 2, MidpointRounding.AwayFromZero);
            if (line.Debit < 0 || line.Credit < 0 || (line.Debit > 0 && line.Credit > 0) ||
                (line.Debit == 0 && line.Credit == 0))
                throw LineError("validation.line_amount", number);

            if (line.CustomerId is not null && line.SupplierId is not null)
                throw LineError("validation.line_party_conflict", number);
            if (account.RequiresCustomer && line.CustomerId is null)
                throw LineError("validation.party_required", number);
            if (account.RequiresSupplier && line.SupplierId is null)
                throw LineError("validation.party_required", number);
            if (account.CostCenterPolicy == CostCenterPolicies.Required && line.CostCenterId is null)
                throw LineError("validation.cost_center_required", number);
            if (account.CostCenterPolicy == CostCenterPolicies.NotAllowed && line.CostCenterId is not null)
                throw LineError("validation.cost_center_not_allowed", number);
        }
    }

    private static ValidationAppException LineError(string key, string line) =>
        new(key, new Dictionary<string, string> { ["line"] = line });
}
