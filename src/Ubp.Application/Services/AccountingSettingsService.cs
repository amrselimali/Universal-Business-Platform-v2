using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

public sealed partial class AccountingSettingsService
{
    private readonly IAccountingSettingsRepository _settings;
    private readonly IAccountRepository _accounts;
    private readonly IJournalRepository _journals;
    private readonly AccountService _accountService;
    private readonly PermissionService _permissions;
    private readonly AuditService _audit;

    public AccountingSettingsService(IAccountingSettingsRepository settings,
        IAccountRepository accounts, IJournalRepository journals, AccountService accountService,
        PermissionService permissions, AuditService audit)
    {
        _settings = settings; _accounts = accounts; _journals = journals;
        _accountService = accountService; _permissions = permissions; _audit = audit;
    }

    public async Task<AccountingSettings> GetAsync(ActorContext actor, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.AccountingSettings, ActionCodes.View, ct: ct);
        return await _settings.GetAsync(ct);
    }

    public async Task<AccountingSettings> SaveAsync(ActorContext actor, AccountingSettings input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.AccountingSettings, ActionCodes.Save, ct: ct);
        var old = await _settings.GetAsync(ct);
        if (old.Id == Guid.Empty) throw new NotFoundAppException();
        if (input.AccountingStartDate.HasValue &&
            input.AccountingStartDate.Value.Date > DateTime.Today.AddYears(1))
            throw new ValidationAppException("validation.start_date_future");

        var updated = await _settings.UpdateAsync(old.Id, input.Version,
            input.AutoPostStockJournals, input.AccountingStartDate?.Date, actor.UserId, ct)
            ?? throw new ConflictException("error.version_conflict");
        await _audit.WriteAsync(actor, "app_settings", old.Id.ToString(), "UPDATE", old, updated, ct);
        return updated;
    }

    public async Task<AccountingSettings> EnableAutoPostAsync(ActorContext actor, bool enabled,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.AccountingSettings,
            ActionCodes.EnableAutoPost, ct: ct);

        if (enabled)
        {
            var report = await BuildCompletenessAsync(ct);
            if (!report.IsComplete)
                throw new ValidationAppException("validation.accounting_setup_incomplete",
                    new Dictionary<string, string> { ["count"] = report.BlockingCount.ToString() });
        }

        var old = await _settings.GetAsync(ct);
        var updated = await _settings.UpdateAsync(old.Id, old.Version, enabled,
            old.AccountingStartDate, actor.UserId, ct) ?? throw new ConflictException();
        await _audit.WriteAsync(actor, "app_settings", old.Id.ToString(), "UPDATE", old, updated, ct);
        return updated;
    }

    public async Task<CompletenessReport> CheckCompletenessAsync(ActorContext actor,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.AccountingSettings,
            ActionCodes.CheckCompleteness, ct: ct);
        return await BuildCompletenessAsync(ct);
    }

    private async Task<CompletenessReport> BuildCompletenessAsync(CancellationToken ct)
    {
        var settings = await _settings.GetAsync(ct);
        var defaults = await _settings.ListDefaultsAsync(ct);
        var report = new CompletenessReport();

        var accountCount = await _accounts.CountAsync(ct);
        report.Items.Add(new CompletenessItem
        {
            Code = "accounts_tree", LabelKey = "accounting.check.accounts_tree",
            Ok = accountCount >= 10, Detail = accountCount.ToString()
        });

        var required = new[]
        {
            PostingKeys.StockInventory, PostingKeys.StockCogs, PostingKeys.OpeningEquity
        };
        var optional = new[]
        {
            PostingKeys.StockAdjustmentGain, PostingKeys.StockAdjustmentLoss, PostingKeys.Rounding
        };

        foreach (var key in required.Concat(optional))
        {
            var entry = defaults.FirstOrDefault(d => d.SettingKey == key);
            var ok = entry?.AccountId is not null;
            if (ok)
            {
                var account = await _accounts.GetAsync(entry!.AccountId!.Value, ct);
                ok = account is { IsActive: true, IsGroup: false };
            }

            var isRequired = required.Contains(key);
            report.Items.Add(new CompletenessItem
            {
                Code = key, LabelKey = $"accounting.check.{key}", Ok = ok,
                Blocking = isRequired, Detail = entry?.AccountName
            });
        }

        var missingCategories = await _settings.CountCategoriesWithoutAccountsAsync(ct);
        report.Items.Add(new CompletenessItem
        {
            Code = "category_accounts", LabelKey = "accounting.check.category_accounts",
            Ok = missingCategories == 0, Detail = missingCategories.ToString()
        });

        var stockJournal = await _journals.GetByTypeAsync(JournalTypes.Stock, ct);
        report.Items.Add(new CompletenessItem
        {
            Code = "stock_journal", LabelKey = "accounting.check.stock_journal",
            Ok = stockJournal is not null, Detail = stockJournal?.Code
        });

        report.Items.Add(new CompletenessItem
        {
            Code = "start_date", LabelKey = "accounting.check.start_date",
            Ok = settings.AccountingStartDate.HasValue,
            Detail = settings.AccountingStartDate?.ToString("yyyy-MM-dd")
        });

        return report;
    }
}
