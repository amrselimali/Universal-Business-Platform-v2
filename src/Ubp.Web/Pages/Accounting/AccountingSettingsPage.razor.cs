using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Web.Pages.Accounting;

public partial class AccountingSettingsPage
{
    [Inject] private AccountingSettingsService SettingsService { get; set; } = default!;
    [Inject] private AccountService Accounts { get; set; } = default!;
    [Inject] private IItemCategoryRepository ItemCategories { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;

    private AccountingSettings _settings = new();
    private DateTime? _startDate;
    private bool _autoPost;
    private IReadOnlyList<Account> _accounts = Array.Empty<Account>();
    private IReadOnlyList<ItemCategoryAccount> _categoryAccounts = Array.Empty<ItemCategoryAccount>();
    private readonly Dictionary<string, Guid?> _defaults = new(StringComparer.OrdinalIgnoreCase);

    protected override string ScreenCode => ScreenCodes.AccountingSettings;
    private bool IsEnglish => T.Language == "en";

    protected override async Task OnLoadAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _settings = await SettingsService.GetAsync(Actor!);
        _startDate = _settings.AccountingStartDate;
        _autoPost = _settings.AutoPostStockJournals;
        _accounts = await Accounts.PostableAsync(Actor!);
        _categoryAccounts = await SettingsService.ListCategoryAccountsAsync(Actor!);

        var defaults = await SettingsService.ListDefaultsAsync(Actor!);
        _defaults.Clear();
        foreach (var key in PostingKeys.All)
            _defaults[key] = defaults.FirstOrDefault(d => d.SettingKey == key)?.AccountId;
    }

    private Guid? DefaultOf(string key) => _defaults.TryGetValue(key, out var id) ? id : null;

    private string AccountName(Guid? accountId) =>
        accountId is null
            ? "—"
            : _accounts.FirstOrDefault(a => a.Id == accountId.Value)?.DisplayName(IsEnglish) ?? "—";

    private async Task SaveAsync() => await RunAsync(async () =>
    {
        _settings.AccountingStartDate = _startDate;
        _settings = await SettingsService.SaveAsync(Actor!, _settings);
        ShowSuccess("common.saved");
    });

    private async Task ToggleAutoPostAsync(bool value) => await RunAsync(async () =>
    {
        _settings = await SettingsService.EnableAutoPostAsync(Actor!, value);
        _autoPost = _settings.AutoPostStockJournals;
        ShowSuccess("common.saved");
    });

    private async Task SaveDefaultAsync(string key, Guid? accountId) => await RunAsync(async () =>
    {
        await SettingsService.SetDefaultAsync(Actor!, key, accountId);
        _defaults[key] = accountId;
        ShowSuccess("common.saved");
    });

    private async Task CheckAsync() => await RunAsync(async () =>
    {
        var report = await SettingsService.CheckCompletenessAsync(Actor!);
        var parameters = new DialogParameters { ["Report"] = report };
        await DialogService.ShowAsync<CompletenessDialog>(T.T("accounting.check_completeness"), parameters);
    });

    private async Task AddCategoryAsync() => await RunAsync(async () =>
    {
        var categories = await ItemCategories.ListOptionsAsync();
        var parameters = new DialogParameters
        {
            ["Categories"] = categories, ["Accounts"] = _accounts
        };
        var dialog = await DialogService.ShowAsync<CategoryAccountDialog>(T.T("accounting.category_accounts"),
            parameters);
        if ((await dialog.Result) is { Canceled: false })
        {
            await LoadAsync();
            ShowSuccess("common.saved");
        }
    });

    private async Task RemoveCategoryAsync(ItemCategoryAccount mapping)
    {
        var confirmed = await DialogService.ShowMessageBoxAsync(T.T("common.confirm_delete"),
            mapping.CategoryName, yesText: T.T("common.delete"), cancelText: T.T("common.cancel"));
        if (confirmed != true) return;

        await RunAsync(async () =>
        {
            await SettingsService.RemoveCategoryAccountAsync(Actor!, mapping.CategoryId);
            await LoadAsync();
            ShowSuccess("common.deleted");
        });
    }
}
