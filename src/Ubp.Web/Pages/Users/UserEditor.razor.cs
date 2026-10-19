using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Web.Services;

namespace Ubp.Web.Pages.Users;

public partial class UserEditor
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;
    [Parameter] public UserDetail? Detail { get; set; }
    [Parameter] public bool CanManagePermissions { get; set; }
    [Inject] private UserService Users { get; set; } = default!;
    [Inject] private BranchService BranchService { get; set; } = default!;
    [Inject] private WarehouseService WarehouseService { get; set; } = default!;
    [Inject] private RoleService RoleService { get; set; } = default!;
    [Inject] private UserContext UserContext { get; set; } = default!;
    [Inject] private UiCulture T { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private MudForm _form = default!;
    private UserInput _input = new() { Language = "ar", IsActive = true };
    private string? _password;
    private List<Branch> _branches = new();
    private List<Warehouse> _warehouses = new();
    private List<Role> _roles = new();
    private IReadOnlyCollection<Guid> _selectedBranches = new List<Guid>();
    private IReadOnlyCollection<Guid> _selectedWarehouses = new List<Guid>();
    private IReadOnlyCollection<Guid> _selectedRoles = new List<Guid>();
    private Guid? _defaultBranch;
    private ActorContext Actor = default!;
    private bool _busy;

    private bool IsEdit => Detail is not null;

    protected override async Task OnInitializedAsync()
    {
        Actor = await UserContext.RequireAsync();
        _branches = (await BranchService.OptionsAsync(Actor)).ToList();
        _warehouses = (await WarehouseService.OptionsAsync(Actor)).ToList();
        _roles = (await RoleService.OptionsAsync(Actor)).ToList();

        if (Detail is not null)
        {
            _input = new UserInput
            {
                Name = Detail.User.Name, Username = Detail.User.Username,
                IsActive = Detail.User.IsActive, Language = Detail.User.Language,
                IsAdmin = Detail.User.IsAdmin, IsSuperAdmin = Detail.User.IsSuperAdmin
            };
            _selectedBranches = Detail.BranchIds.ToList();
            _selectedWarehouses = Detail.WarehouseIds.ToList();
            _selectedRoles = Detail.RoleIds.ToList();
            _defaultBranch = Detail.DefaultBranchId;
        }
    }

    private string RoleName(Role role) =>
        T.Language == "en" && !string.IsNullOrWhiteSpace(role.NameEn) ? role.NameEn! : role.NameAr;

    private void Cancel() => MudDialog.Cancel();

    private async Task SaveAsync()
    {
        await _form.Validate();
        if (!_form.IsValid) return;
        if (!IsEdit && string.IsNullOrWhiteSpace(_password))
        {
            Snackbar.Add(T.T("validation.password_required"), Severity.Error);
            return;
        }

        _busy = true;
        try
        {
            var actor = await UserContext.RequireAsync();
            _input.Password = string.IsNullOrWhiteSpace(_password) ? null : _password;
            _input.BranchIds = _selectedBranches.ToList();
            _input.DefaultBranchId = _defaultBranch;
            _input.WarehouseIds = _selectedWarehouses.ToList();
            _input.RoleIds = _selectedRoles.ToList();

            UserDetail saved = Detail is null
                ? await Users.CreateAsync(actor, _input)
                : await Users.UpdateAsync(actor, Detail.User.Id, Detail.User.Version, _input);
            MudDialog.Close(DialogResult.Ok(saved));
        }
        catch (AppException ex)
        {
            var text = T.T(ex.Code);
            foreach (var arg in ex.Args) text = text.Replace("{" + arg.Key + "}", arg.Value);
            Snackbar.Add(text, Severity.Error);
        }
        finally { _busy = false; }
    }
}
