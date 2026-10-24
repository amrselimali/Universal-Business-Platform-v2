using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Web.Services;

namespace Ubp.Web.Pages.Accounting;

public partial class AccountDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;
    [Parameter] public Account? Model { get; set; }
    [Parameter] public Guid? ParentId { get; set; }
    [Parameter] public string? ParentName { get; set; }

    [Inject] private AccountService Accounts { get; set; } = default!;
    [Inject] private UserContext UserContext { get; set; } = default!;
    [Inject] private UiCulture T { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private MudForm _form = default!;
    private AccountInput _input = new();
    private bool _busy;
    private bool IsSystem => Model?.IsSystem == true;

    protected override void OnInitialized()
    {
        if (Model is null) return;
        _input = new AccountInput
        {
            Code = Model.Code, NameAr = Model.NameAr, NameEn = Model.NameEn,
            ParentId = Model.ParentId, IsGroup = Model.IsGroup, AccountType = Model.AccountType,
            CostCenterPolicy = Model.CostCenterPolicy, Notes = Model.Notes, IsActive = Model.IsActive
        };
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
            var saved = Model is not null
                ? await Accounts.UpdateAsync(actor, Model.Id, Model.Version, _input)
                : ParentId is null
                    ? await Accounts.CreateRootAsync(actor, _input)
                    : await Accounts.CreateChildAsync(actor, ParentId.Value, _input);
            MudDialog.Close(DialogResult.Ok(saved));
        }
        catch (AppException ex) { Snackbar.Add(T.T(ex.Code), Severity.Error); }
        finally { _busy = false; }
    }
}
