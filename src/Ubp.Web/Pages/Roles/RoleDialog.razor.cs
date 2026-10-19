using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Web.Services;

namespace Ubp.Web.Pages.Roles;

public partial class RoleDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;
    [Parameter] public Role? Model { get; set; }
    [Parameter] public IReadOnlyList<PermissionEntry> Permissions { get; set; } = Array.Empty<PermissionEntry>();
    [Parameter] public bool CanManagePermissions { get; set; }
    [Inject] private RoleService Roles { get; set; } = default!;
    [Inject] private UserContext UserContext { get; set; } = default!;
    [Inject] private UiCulture T { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private sealed class Row
    {
        public bool Allowed { get; set; }
        public short Scope { get; set; } = PermissionScope.Branches;
    }

    private MudForm _form = default!;
    private RoleInput _input = new() { IsActive = true };
    private readonly Dictionary<string, Row> _rows = new(StringComparer.OrdinalIgnoreCase);
    private List<Role> _roleOptions = new();
    private Guid? _copyFrom;
    private bool _busy;

    private bool IsEdit => Model is not null;

    protected override async Task OnInitializedAsync()
    {
        foreach (var screen in AppRegistry.Screens)
        {
            foreach (var action in screen.Actions)
                _rows[$"{screen.Code}/{action.Code}"] = new Row();
        }

        if (Model is not null)
        {
            _input = new RoleInput
            {
                Code = Model.Code, NameAr = Model.NameAr, NameEn = Model.NameEn,
                IsActive = Model.IsActive
            };
        }
        foreach (var permission in Permissions.Where(p => p.IsAllowed))
        {
            var key = $"{permission.ScreenCode}/{permission.ActionCode}";
            if (_rows.TryGetValue(key, out var row))
            {
                row.Allowed = true;
                row.Scope = PermissionScope.Normalize(permission.Scope);
            }
        }

        var actor = await UserContext.RequireAsync();
        _roleOptions = (await Roles.OptionsAsync(actor)).ToList();
    }

    private string NameOf(Role role) =>
        T.Language == "en" && !string.IsNullOrWhiteSpace(role.NameEn) ? role.NameEn! : role.NameAr;

    private void SelectAll(ScreenDefinition screen, bool allowed)
    {
        foreach (var action in screen.Actions)
            _rows[$"{screen.Code}/{action.Code}"].Allowed = allowed;
    }

    private async Task CopyAsync()
    {
        if (_copyFrom is null) return;
        var actor = await UserContext.RequireAsync();
        var source = await Roles.GetPermissionsAsync(actor, _copyFrom.Value);
        foreach (var row in _rows.Values) row.Allowed = false;
        foreach (var permission in source.Where(p => p.IsAllowed))
        {
            var key = $"{permission.ScreenCode}/{permission.ActionCode}";
            if (_rows.TryGetValue(key, out var row))
            {
                row.Allowed = true;
                row.Scope = PermissionScope.Normalize(permission.Scope);
            }
        }
    }

    private void Cancel() => MudDialog.Cancel();

    private async Task SaveAsync()
    {
        await _form.Validate();
        if (!_form.IsValid) return;
        _busy = true;
        try
        {
            var actor = await UserContext.RequireAsync();
            _input.Permissions = _rows
                .Where(p => p.Value.Allowed)
                .Select(p => new PermissionEntry
                {
                    ScreenCode = p.Key.Split('/')[0],
                    ActionCode = p.Key.Split('/')[1],
                    IsAllowed = true,
                    Scope = p.Value.Scope
                }).ToList();
            Role saved = Model is null
                ? await Roles.CreateAsync(actor, _input)
                : await Roles.UpdateAsync(actor, Model.Id, Model.Version, _input);
            MudDialog.Close(DialogResult.Ok(saved));
        }
        catch (AppException ex)
        {
            Snackbar.Add(T.T(ex.Code), Severity.Error);
        }
        finally { _busy = false; }
    }
}
