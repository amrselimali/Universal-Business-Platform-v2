using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Security;
using Ubp.Core.Results;

namespace Ubp.Web.Pages.Users;

public partial class Index
{
    [Inject] private UserService UsersService { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;

    private MudTable<UserListItem> _table = default!;
    private string? _search;
    private bool _includeInactive;

    protected override string ScreenCode => ScreenCodes.Users;

    protected override Task OnLoadAsync() => Task.CompletedTask;

    private static string Initial(UserListItem user) =>
        (string.IsNullOrWhiteSpace(user.Name) ? user.Username : user.Name)[..1].ToUpperInvariant();

    private async Task<TableData<UserListItem>> LoadServerData(TableState state, CancellationToken ct)
    {
        var request = new PageRequest
        {
            Page = state.Page + 1, PageSize = 25, Search = _search,
            IncludeInactive = _includeInactive, SortBy = state.SortLabel ?? "username",
            Descending = state.SortDirection == SortDirection.Descending
        };
        var result = await UsersService.ListAsync(Actor!, request, ct);
        return new TableData<UserListItem> { Items = result.Items, TotalItems = result.Total };
    }

    private async Task ReloadAsync() => await _table.ReloadServerData();
    private async Task OnFilterChanged(bool value) { _includeInactive = value; await ReloadAsync(); }

    private async Task AddAsync()
    {
        var dialog = await DialogService.ShowAsync<UserEditor>(T.T("users.add"),
            new DialogParameters { ["CanManagePermissions"] = Can(ActionCodes.ManagePermissions) },
            new DialogOptions { MaxWidth = MaxWidth.ExtraLarge, FullWidth = true });
        if ((await dialog.Result) is { Canceled: false })
        {
            await _table.ReloadServerData();
            ShowSuccess("user.saved");
        }
    }

    private async Task EditAsync(UserListItem user)
    {
        try
        {
            var detail = await UsersService.GetAsync(Actor!, user.Id);
            var dialog = await DialogService.ShowAsync<UserEditor>(T.T("users.edit"),
                new DialogParameters
                {
                    ["Detail"] = detail,
                    ["CanManagePermissions"] = Can(ActionCodes.ManagePermissions)
                },
                new DialogOptions { MaxWidth = MaxWidth.ExtraLarge, FullWidth = true });
            if ((await dialog.Result) is { Canceled: false })
            {
                await _table.ReloadServerData();
                ShowSuccess("user.saved");
            }
        }
        catch (AppException ex) { ShowError(ex); }
    }

    private async Task ResetPasswordAsync(UserListItem user)
    {
        var dialog = await DialogService.ShowAsync<ResetPasswordDialog>(T.T("users.reset_password"),
            new DialogParameters { ["UserId"] = user.Id, ["Username"] = user.Username });
        if ((await dialog.Result) is { Canceled: false })
            ShowSuccess("user.password_reset");
    }

    private async Task UnlockAsync(UserListItem user) =>
        await RunAsync(async () =>
        {
            await UsersService.UnlockAsync(Actor!, user.Id);
            await _table.ReloadServerData();
            ShowSuccess("user.unlocked");
        });

    private async Task ToggleAsync(UserListItem user, bool value) =>
        await RunAsync(async () =>
        {
            await UsersService.SetActiveAsync(Actor!, user.Id, value);
            await _table.ReloadServerData();
            ShowSuccess("common.saved");
        });

    private async Task DeleteAsync(UserListItem user)
    {
        var confirmed = await DialogService.ShowMessageBoxAsync(T.T("common.confirm_delete"),
            T.T("users.delete_confirm", user.Username), yesText: T.T("common.delete"),
            cancelText: T.T("common.cancel"));
        if (confirmed != true) return;
        await RunAsync(async () =>
        {
            var outcome = await UsersService.DeleteAsync(Actor!, user.Id);
            await _table.ReloadServerData();
            ShowSuccess(outcome.MessageKey);
        });
    }
}
