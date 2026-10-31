using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

public sealed record AssetDisposalResult(Guid AssetId, Guid JournalEntryId, decimal SaleAmount,
    decimal Gain, decimal Loss);

public sealed partial class AssetService
{
    /// <summary>
    /// استبعاد/بيع أصل: يُلغي التكلفة ويُخرج مجمع الإهلاك ويُسجّل المقابل النقدي،
    /// والفرق يُرحَّل ربحاً أو خسارة (حسابات posting_defaults: asset_disposal_gain/loss).
    /// </summary>
    public async Task<AssetDisposalResult> DisposeAsync(ActorContext actor, Guid assetId,
        decimal saleAmount, DateTime disposalDate, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Assets, ActionCodes.Edit, ct: ct);
        var asset = await _assets.GetAsync(assetId, ct) ?? throw new NotFoundAppException();
        EnsureVisible(actor, asset);
        if (asset.Status == AssetStatuses.Disposed)
            throw new ValidationAppException("validation.asset_disposed");
        if (saleAmount < 0) throw new ValidationAppException("validation.asset_cost_invalid");
        if (asset.BranchId is not { } branchId)
            throw new ValidationAppException("validation.asset_shared_not_disposable");

        var accumulated = Math.Round(await _assets.AccumulatedAsync(assetId, ct), 2);
        var bookValue = Math.Round(asset.Cost - accumulated, 2);
        var difference = Math.Round(saleAmount - bookValue, 2);
        var gain = difference > 0 ? difference : 0m;
        var loss = difference < 0 ? -difference : 0m;

        var category = (await _assets.ListCategoriesAsync(ct)).FirstOrDefault(c => c.Id == asset.CategoryId)
            ?? throw new NotFoundAppException();

        var lines = new List<JournalEntryLineInput>();
        if (saleAmount > 0)
        {
            var cash = await _assets.BranchCashAccountAsync(branchId, ct);
            if (cash is null) throw new ValidationAppException("validation.cash_account_required");
            lines.Add(new JournalEntryLineInput { AccountId = cash.Value, Debit = saleAmount });
        }
        if (accumulated > 0 && category.AccumAccountId is { } accum)
            lines.Add(new JournalEntryLineInput { AccountId = accum, Debit = accumulated });

        var assetAccount = category.AssetAccountId
            ?? throw new ValidationAppException("validation.asset_account_required");
        lines.Add(new JournalEntryLineInput { AccountId = assetAccount, Credit = asset.Cost });

        if (gain > 0)
        {
            var account = await _assets.DefaultAccountAsync("asset_disposal_gain", ct)
                ?? throw new ValidationAppException("validation.disposal_gain_required");
            lines.Add(new JournalEntryLineInput { AccountId = account, Credit = gain });
        }
        if (loss > 0)
        {
            var account = await _assets.DefaultAccountAsync("asset_disposal_loss", ct)
                ?? throw new ValidationAppException("validation.disposal_loss_required");
            lines.Add(new JournalEntryLineInput { AccountId = account, Debit = loss });
        }

        var journal = await _journals.GetByCodeAsync("GEN", ct)
            ?? throw new ValidationAppException("validation.journal_missing");
        var draft = await _journalEntries.CreateAsync(actor, new JournalEntryInput
        {
            JournalId = journal.Id,
            BranchId = branchId,
            EntryDate = disposalDate.Date,
            Reference = $"DISP-{asset.Code}",
            Description = $"استبعاد الأصل {asset.Code}",
            Lines = lines
        }, ct);
        var posted = await _journalEntries.PostAsync(actor, draft.Header.Id, ct);

        if (!await _assets.MarkDisposedAsync(assetId, saleAmount, posted.Id, actor.UserId, ct))
            throw new ValidationAppException("validation.asset_disposed");

        await _audit.WriteAsync(actor, "fixed_assets", assetId.ToString(), "UPDATE", asset,
            new { Status = AssetStatuses.Disposed, saleAmount, gain, loss }, ct);

        return new AssetDisposalResult(assetId, posted.Id, saleAmount, gain, loss);
    }

    /// <summary>مزامنة قراءات العدّاد من أسطر فواتير البيع المرحّلة (مرة واحدة لكل سطر).</summary>
    public async Task<int> SyncSalesReadingsAsync(ActorContext actor, DateTime month,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Assets, ActionCodes.AddMeterReading, ct: ct);
        var from = new DateOnly(month.Year, month.Month, 1);
        var to = from.AddMonths(1).AddDays(-1);
        var scope = actor.IsSuperAdmin ? null : actor.CurrentBranchId;
        var synced = await _assets.SyncSalesReadingsAsync(scope, from, to, actor.UserId, ct);
        if (synced > 0)
            await _audit.WriteAsync(actor, "asset_meter_readings", from.ToString("yyyy-MM"),
                "INSERT", null, new { SyncedAssets = synced }, ct);
        return synced;
    }
}
