using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Web.Services;

namespace Ubp.Web.Pages.Users;

public partial class UserPermissions
{
    [Parameter, EditorRequired] public Guid UserId { get; set; }
    [Parameter] public bool CanEdit { get; set; }
    [Inject] private UserService Users { get; set; } = default!;
    [Inject] private UserContext UserContext { get; set; } = default!;
    [Inject] private UiCulture T { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private sealed class Row
    {
        public EffectivePermission Source { get; init; } = new();
        public bool Allowed { get; set; }
        public short Scope { get; set; }
    }

    private readonly Dictionary<string, Row> _rows = new(StringComparer.OrdinalIgnoreCase);
    private List<UserListItem> _copyUsers = new();
    private Guid? _copyFrom;
    private bool _loading = true;
    private bool _busy;

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
        var actor = await UserContext.RequireAsync();
        var users = await Users.ListAsync(actor, new PageRequest { PageSize = 25 });
        _copyUsers = users.Items.Where(u => u.Id != UserId).ToList();
    }

    private async Task LoadAsync()
    {
        _loading = true;
        var actor = await UserContext.RequireAsync();
        var matrix = await Users.GetPermissionMatrixAsync(actor, UserId);
        _rows.Clear();
        foreach (var item in matrix)
        {
            _rows[$"{item.ScreenCode}/{item.ActionCode}"] = new Row
            {
                Source = item, Allowed = item.Allowed, Scope = item.Scope
            };
        }
        _loading = false;
    }

    private void SetAllowed(string key, bool allowed)
    {
        _rows[key].Allowed = allowed;
        if (allowed && _rows[key].Scope < 0) _rows[key].Scope = PermissionScope.Branches;
    }

    private void SetScope(string key, short scope) => _rows[key].Scope = scope;

    private void SelectAll(ScreenDefinition screen, bool allowed)
    {
        foreach (var action in screen.Actions)
        {
            var key = $"{screen.Code}/{action.Code}";
            _rows[key].Allowed = allowed;
            if (allowed && _rows[key].Scope is < 0 or > 2) _rows[key].Scope = PermissionScope.Branches;
        }
    }

    private async Task SaveAsync()
    {
        _busy = true;
        try
        {
            var actor = await UserContext.RequireAsync();
            var entries = _rows.Select(pair => new PermissionEntry
            {
                ScreenCode = pair.Value.Source.ScreenCode,
                ActionCode = pair.Value.Source.ActionCode,
                IsAllowed = pair.Value.Allowed,
                Scope = PermissionScope.Normalize(pair.Value.Scope)
            });
            await Users.SavePermissionsAsync(actor, UserId, entries);
            await LoadAsync();
            Snackbar.Add(T.T("permissions.saved"), Severity.Success);
        }
        catch (AppException ex)
        {
            Snackbar.Add(T.T(ex.Code), Severity.Error);
        }
        catch (Exception)
        {
            Snackbar.Add(T.T("error.generic"), Severity.Error);
        }
        finally { _busy = false; }
    }

    private async Task CopyAsync()
    {
        if (_copyFrom is null) return;
        _busy = true;
        try
        {
            var actor = await UserContext.RequireAsync();
            await Users.CopyPermissionsAsync(actor, _copyFrom.Value, UserId);
            await LoadAsync();
            Snackbar.Add(T.T("permissions.copied"), Severity.Success);
        }
        catch (AppException ex)
        {
            Snackbar.Add(T.T(ex.Code), Severity.Error);
        }
        finally { _busy = false; }
    }
}
