using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Security;

namespace Ubp.Web.Pages.Roles;

public partial class Index
{
    [Inject] private RoleService RolesService { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;

    private MudTable<Role> _table = default!;
    private string? _search;
    private bool _includeInactive;

    protected override string ScreenCode => ScreenCodes.Roles;

    protected override Task OnLoadAsync() => Task.CompletedTask;

    private string NameOf(Role role) =>
        T.Language == "en" && !string.IsNullOrWhiteSpace(role.NameEn) ? role.NameEn! : role.NameAr;

    private async Task<TableData<Role>> LoadServerData(TableState state, CancellationToken ct)
    {
        var request = new PageRequest
        {
            Page = state.Page + 1, PageSize = 25, Search = _search,
            IncludeInactive = _includeInactive, SortBy = state.SortLabel ?? "code",
            Descending = state.SortDirection == SortDirection.Descending
        };
        var result = await RolesService.ListAsync(Actor!, request, ct);
        return new TableData<Role> { Items = result.Items, TotalItems = result.Total };
    }

    private async Task ReloadAsync() => await _table.ReloadServerData();
    private async Task OnFilterChanged(bool value) { _includeInactive = value; await ReloadAsync(); }

    private async Task AddAsync()
    {
        var dialog = await DialogService.ShowAsync<RoleDialog>(T.T("roles.add"),
            new DialogParameters { ["CanManagePermissions"] = Can(ActionCodes.ManagePermissions) },
            new DialogOptions { MaxWidth = MaxWidth.ExtraLarge, FullWidth = true });
        if ((await dialog.Result) is { Canceled: false })
        {
            await _table.ReloadServerData();
            ShowSuccess("role.saved");
        }
    }

    private async Task EditAsync(Role role)
    {
        var permissions = await RolesService.GetPermissionsAsync(Actor!, role.Id);
        var dialog = await DialogService.ShowAsync<RoleDialog>(T.T("roles.edit"),
            new DialogParameters
            {
                ["Model"] = role,
                ["Permissions"] = permissions,
                ["CanManagePermissions"] = Can(ActionCodes.ManagePermissions)
            },
            new DialogOptions { MaxWidth = MaxWidth.ExtraLarge, FullWidth = true });
        if ((await dialog.Result) is { Canceled: false })
        {
            await _table.ReloadServerData();
            ShowSuccess("role.saved");
        }
    }

    private async Task ToggleAsync(Role role, bool value) =>
        await RunAsync(async () =>
        {
            await RolesService.SetActiveAsync(Actor!, role.Id, value);
            await _table.ReloadServerData();
            ShowSuccess("common.saved");
        });

    private async Task DeleteAsync(Role role)
    {
        var confirmed = await DialogService.ShowMessageBoxAsync(T.T("common.confirm_delete"),
            T.T("roles.delete_confirm", role.NameAr), yesText: T.T("common.delete"),
            cancelText: T.T("common.cancel"));
        if (confirmed != true) return;
        await RunAsync(async () =>
        {
            var outcome = await RolesService.DeleteAsync(Actor!, role.Id);
            await _table.ReloadServerData();
            ShowSuccess(outcome.MessageKey);
        });
    }
}
