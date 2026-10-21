using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Web.Services;

namespace Ubp.Web.Pages.Inventory;

public partial class UnitDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;
    [Parameter] public Unit? Model { get; set; }
    [Inject] private UnitService Units { get; set; } = default!;
    [Inject] private UserContext UserContext { get; set; } = default!;
    [Inject] private UiCulture T { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private MudForm _form = default!;
    private UnitInput _input = new();
    private bool _busy;
    private bool IsEdit => Model is not null;

    protected override void OnInitialized()
    {
        if (Model is null) return;
        _input = new UnitInput
        {
            Code = Model.Code, NameAr = Model.NameAr, NameEn = Model.NameEn,
            Decimals = Model.Decimals, IsActive = Model.IsActive
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
            var saved = Model is null
                ? await Units.CreateAsync(actor, _input)
                : await Units.UpdateAsync(actor, Model.Id, Model.Version, _input);
            MudDialog.Close(DialogResult.Ok(saved));
        }
        catch (AppException ex) { Snackbar.Add(T.T(ex.Code), Severity.Error); }
        finally { _busy = false; }
    }
}
