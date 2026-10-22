using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Security;

namespace Ubp.Web.Pages.Accounting;

public partial class Accounts
{
    [Inject] private AccountService AccountService { get; set; } = default!;
    [Inject] private AccountingSettingsService AccountingSettings { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;

    private MudTable<Account> _table = default!;
    private string? _search;
    private string? _typeFilter;
    private bool _includeInactive;
    private bool IsEnglish => T.Language == "en";

    protected override string ScreenCode => ScreenCodes.Accounts;
    protected override Task OnLoadAsync() => Task.CompletedTask;

    private string PolicyKey(Account account) => account.CostCenterPolicy switch
    {
        CostCenterPolicies.Required => "accounting.policy_required",
        CostCenterPolicies.Optional => "accounting.policy_optional",
        _ => "accounting.policy_not_allowed"
    };

    private async Task<TableData<Account>> LoadServerData(TableState state, CancellationToken ct)
    {
        var result = await AccountService.ListAsync(Actor!, new AccountFilter
        {
            Search = _search, IncludeInactive = _includeInactive, AccountType = _typeFilter
        }, new PageRequest
        {
            Page = state.Page + 1, PageSize = 25,
            SortBy = state.SortLabel ?? "path",
            Descending = state.SortDirection == SortDirection.Descending
        }, ct);
        return new TableData<Account> { Items = result.Items, TotalItems = result.Total };
    }

    private async Task ReloadAsync() => await _table.ReloadServerData();

    private async Task OnFilterChanged(bool value)
    {
        _includeInactive = value;
        await ReloadAsync();
    }

    private Task AddRootAsync() => OpenDialogAsync(null, null);

    private Task AddChildAsync(Account parent) => OpenDialogAsync(null, parent);

    private Task EditAsync(Account account) => OpenDialogAsync(account, null);

    private async Task OpenDialogAsync(Account? model, Account? parent)
    {
        var parameters = new DialogParameters
        {
            ["Model"] = model,
            ["ParentId"] = parent?.Id,
            ["ParentName"] = parent?.NameAr
        };
        var dialog = await DialogService.ShowAsync<AccountDialog>(T.T("screen.accounts"), parameters);
        if ((await dialog.Result) is { Canceled: false })
        {
            await ReloadAsync();
            ShowSuccess("common.saved");
        }
    }

    private async Task ToggleAsync(Account account, bool value) => await RunAsync(async () =>
    {
        await AccountService.SetActiveAsync(Actor!, account.Id, value);
        await ReloadAsync();
        ShowSuccess("common.saved");
    });

    private async Task DeleteAsync(Account account)
    {
        var confirmed = await DialogService.ShowMessageBoxAsync(T.T("common.confirm_delete"),
            $"{account.Code} — {account.NameAr}", yesText: T.T("common.delete"),
            cancelText: T.T("common.cancel"));
        if (confirmed != true) return;

        await RunAsync(async () =>
        {
            await AccountService.DeleteAsync(Actor!, account.Id);
            await ReloadAsync();
            ShowSuccess("common.deleted");
        });
    }

    private async Task MoveAsync(Account account) => await RunAsync(async () =>
    {
        var groups = await AccountService.TreeAsync(Actor!, false);
        var options = groups
            .Where(a => a.IsGroup && a.Id != account.Id)
            .Select(a => new TreeMoveOption(a.Id, $"{a.Code} — {a.DisplayName(IsEnglish)}", a.Level))
            .ToList();
        var parameters = new DialogParameters
        {
            ["Options"] = options, ["Title"] = T.T("accounting.move"), ["AllowRoot"] = true
        };
        var dialog = await DialogService.ShowAsync<TreeMoveDialog>(T.T("accounting.move"), parameters);
        var result = await dialog.Result;
        if (result is { Canceled: true }) return;

        await AccountService.MoveAsync(Actor!, account.Id,
            result?.Data is Guid parent && parent != Guid.Empty ? parent : null);
        await ReloadAsync();
        ShowSuccess("common.saved");
    });

    private async Task LoadDefaultsAsync()
    {
        var confirmed = await DialogService.ShowMessageBoxAsync(T.T("accounting.load_default_tree"),
            T.T("accounting.load_default_tree_confirm"), yesText: T.T("common.confirm"),
            cancelText: T.T("common.cancel"));
        if (confirmed != true) return;

        await RunAsync(async () =>
        {
            var created = await AccountingSettings.LoadDefaultTreeAsync(Actor!);
            await ReloadAsync();
            Snackbar.Add($"{T.T("common.saved")} ({created})", Severity.Success);
        });
    }
}
