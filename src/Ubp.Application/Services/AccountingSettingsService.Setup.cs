using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;

namespace Ubp.Application.Services;

public sealed partial class AccountingSettingsService
{
    /// <summary>ربط مفاتيح الترحيل بالحسابات؛ لا يُقبل إلا حساب قابل للترحيل ونشط.</summary>
    public async Task SetDefaultAsync(ActorContext actor, string settingKey, Guid? accountId,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.AccountingSettings, ActionCodes.Save, ct: ct);
        var types = await _settings.ListTypesAsync(ct);
        if (!types.Any(t => string.Equals(t.Key, settingKey, StringComparison.OrdinalIgnoreCase)))
            throw new ValidationAppException("validation.posting_key");

        if (accountId.HasValue)
        {
            var account = await _accounts.GetAsync(accountId.Value, ct)
                ?? throw new NotFoundAppException("validation.account_not_found");
            if (account.IsGroup || !account.IsActive)
                throw new ValidationAppException("validation.account_not_postable");
        }

        await _settings.SetDefaultAsync(settingKey, accountId, actor.UserId, ct);
        PostingReadinessService.InvalidateAll();
        await _audit.WriteAsync(actor, "posting_defaults", settingKey, "UPDATE", null,
            new { settingKey, accountId }, ct);
    }

    public async Task<IReadOnlyList<PostingDefaultType>> ListTypesAsync(ActorContext actor,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.AccountingSettings, ActionCodes.View, ct: ct);
        return await _settings.ListTypesAsync(ct);
    }

    public async Task<IReadOnlyList<PostingDefault>> ListDefaultsAsync(ActorContext actor,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.AccountingSettings, ActionCodes.View, ct: ct);
        return await _settings.ListDefaultsAsync(ct);
    }

    public async Task<IReadOnlyList<ItemCategoryAccount>> ListCategoryAccountsAsync(ActorContext actor,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.AccountingSettings, ActionCodes.View, ct: ct);
        return await _settings.ListCategoryAccountsAsync(ct);
    }

    public async Task SaveCategoryAccountAsync(ActorContext actor, ItemCategoryAccountInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.AccountingSettings, ActionCodes.Save, ct: ct);
        if (input.InventoryAccountId == Guid.Empty || input.CogsAccountId == Guid.Empty)
            throw new ValidationAppException("validation.required_fields");

        foreach (var accountId in new[]
                 {
                     input.InventoryAccountId, input.CogsAccountId,
                     input.AdjustmentGainAccountId, input.AdjustmentLossAccountId
                 }.Where(id => id.HasValue))
        {
            var account = await _accounts.GetAsync(accountId!.Value, ct)
                ?? throw new NotFoundAppException("validation.account_not_found");
            if (account.IsGroup || !account.IsActive)
                throw new ValidationAppException("validation.account_not_postable");
        }

        try
        {
            await _settings.UpsertCategoryAccountAsync(Guid.CreateVersion7(), input, actor.UserId, ct);
            await _audit.WriteAsync(actor, "item_category_accounts", input.CategoryId.ToString(),
                "UPDATE", null, input, ct);
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }

    public async Task<MutationOutcome> RemoveCategoryAccountAsync(ActorContext actor, Guid categoryId,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.AccountingSettings, ActionCodes.Save, ct: ct);
        if (!await _settings.DeleteCategoryAccountAsync(categoryId, ct))
            throw new NotFoundAppException();
        await _audit.WriteAsync(actor, "item_category_accounts", categoryId.ToString(),
            "DELETE", null, null, ct);
        return new MutationOutcome(true, false, "common.deleted");
    }

    /// <summary>تحميل الشجرة القياسية ثم ملء حسابات الترحيل الافتراضية الناقصة فقط.</summary>
    public async Task<int> LoadDefaultTreeAsync(ActorContext actor, CancellationToken ct = default)
    {
        var created = await _accountService.SeedDefaultTreeAsync(actor, ct);

        var mapping = new Dictionary<string, string>
        {
            [PostingKeys.StockInventory] = "2150",
            [PostingKeys.StockCogs] = "5100",
            [PostingKeys.OpeningEquity] = "3100",
            [PostingKeys.StockAdjustmentGain] = "5410",
            [PostingKeys.StockAdjustmentLoss] = "5420",
            [PostingKeys.Rounding] = "5490"
        };

        var existing = (await _settings.ListDefaultsAsync(ct))
            .Where(d => d.AccountId.HasValue)
            .Select(d => d.SettingKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, code) in mapping)
        {
            if (existing.Contains(key)) continue;
            var account = await _accounts.GetByCodeAsync(code, ct);
            if (account is null || account.IsGroup) continue;
            await _settings.SetDefaultAsync(key, account.Id, actor.UserId, ct);
        }

        await _audit.WriteAsync(actor, "posting_defaults", "default-setup", "INSERT", null,
            new { created }, ct);
        return created;
    }
}
