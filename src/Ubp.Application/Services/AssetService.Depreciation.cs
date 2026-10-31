using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

public sealed record AssetDepreciationResult(Guid RunId, Guid? JournalEntryId, decimal Total,
    int AssetsDepreciated);

public sealed partial class AssetService
{
    /// <summary>
    /// إهلاك شهري/سنوي (حسب تصنيف الأصل) مع **قيد محاسبي** واحد لكل تشغيل:
    /// مدين مصروف الإهلاك / دائن مجمع الإهلاك، ويُمنع تكرار نفس الشهر لنفس الفرع.
    /// الأصول المشتركة (بلا فرع) تُستثنى من التشغيل الفرعي لتفادي الإهلاك المزدوج.
    /// </summary>
    public async Task<AssetDepreciationResult> RunDepreciationAsync(ActorContext actor,
        DateTime periodMonth, Guid branchId, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Assets, ActionCodes.RunDepreciation, ct: ct);
        BranchScope.Ensure(actor, branchId);

        var month = new DateOnly(periodMonth.Year, periodMonth.Month, 1);
        var monthEnd = month.AddMonths(1).AddDays(-1);
        if (await _assets.RunExistsAsync(month, branchId, ct))
            throw new ConflictException("validation.depreciation_already_run");

        var candidates = await _assets.ListDepreciableAsync(branchId, monthEnd, ct);
        var lines = new List<(Guid AssetId, decimal Amount, decimal Accumulated)>();
        foreach (var asset in candidates)
        {
            var depreciable = asset.Cost - asset.SalvageValue;
            var remaining = depreciable - asset.Accumulated;
            if (remaining <= 0) continue;

            decimal amount;
            if (string.Equals(asset.Method, DepreciationMethods.Units, StringComparison.OrdinalIgnoreCase))
            {
                // طريقة الوحدات: القسط = وحدات الشهر × (القابل للإهلاك ÷ الوحدات المتوقعة)
                if (asset.ExpectedUnits is null or <= 0) continue;
                var units = await _assets.MeterDeltaAsync(asset.AssetId, month,
                    month.AddMonths(1), ct);
                amount = Math.Round(units * depreciable / asset.ExpectedUnits.Value, 2,
                    MidpointRounding.AwayFromZero);
            }
            else
            {
                if (asset.UsefulLifeMonths <= 0) continue;
                var monthly = Math.Round(depreciable / asset.UsefulLifeMonths, 2,
                    MidpointRounding.AwayFromZero);
                var annual = string.Equals(asset.Frequency, DepreciationFrequencies.Annual,
                    StringComparison.OrdinalIgnoreCase);
                amount = annual ? (month.Month == 12 ? monthly * 12 : 0m) : monthly;
            }

            amount = Math.Min(amount, remaining);
            if (amount <= 0) continue;

            lines.Add((asset.AssetId, amount, asset.Accumulated + amount));
        }

        if (lines.Count == 0)
            throw new ValidationAppException("validation.nothing_to_depreciate");

        var journal = await _journals.GetByCodeAsync("DEP", ct)
            ?? throw new ValidationAppException("validation.depreciation_journal_missing");

        var amounts = lines.ToDictionary(l => l.AssetId, l => l.Amount);
        var entryLines = candidates
            .Where(c => amounts.ContainsKey(c.AssetId))
            .GroupBy(c => new { c.ExpenseAccountId, c.AccumAccountId })
            .SelectMany(g => new[]
            {
                new JournalEntryLineInput
                {
                    AccountId = g.Key.ExpenseAccountId, Debit = g.Sum(c => amounts[c.AssetId])
                },
                new JournalEntryLineInput
                {
                    AccountId = g.Key.AccumAccountId, Credit = g.Sum(c => amounts[c.AssetId])
                }
            })
            .ToList();

        var total = lines.Sum(l => l.Amount);
        var draft = await _journalEntries.CreateAsync(actor, new JournalEntryInput
        {
            JournalId = journal.Id,
            BranchId = branchId,
            EntryDate = monthEnd.ToDateTime(TimeOnly.MinValue),
            Reference = $"DEP-{month:yyyyMM}",
            Description = $"إهلاك الأصول {month:yyyy-MM}",
            Lines = entryLines
        }, ct);
        var posted = await _journalEntries.PostAsync(actor, draft.Header.Id, ct);

        var runId = await _assets.InsertRunAsync(month, branchId, total, posted.Id, actor.UserId, ct);
        foreach (var line in lines)
            await _assets.InsertLineAsync(runId, line.AssetId, month, line.Amount, line.Accumulated, ct);

        await _audit.WriteAsync(actor, "asset_depreciation_runs", runId.ToString(), "INSERT", null,
            new { month, branchId, total, Assets = lines.Count }, ct);

        return new AssetDepreciationResult(runId, posted.Id, total, lines.Count);
    }
}
