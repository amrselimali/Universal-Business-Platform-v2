using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Web.Services;

namespace Ubp.Web.Pages.Accounting;

public partial class CostCenterDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;
    [Parameter] public CostCenter? Model { get; set; }
    [Parameter] public Guid? ParentId { get; set; }
    [Parameter] public string? ParentName { get; set; }

    [Inject] private CostCenterService CostCenters { get; set; } = default!;
    [Inject] private BranchService BranchService { get; set; } = default!;
    [Inject] private UserContext UserContext { get; set; } = default!;
    [Inject] private UiCulture T { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private MudForm _form = default!;
    private CostCenterInput _input = new();
    private IReadOnlyList<Branch> _branches = Array.Empty<Branch>();
    private bool _busy;

    protected override async Task OnInitializedAsync()
    {
        var actor = await UserContext.GetAsync();
        if (actor is not null) _branches = await BranchService.OptionsAsync(actor);

        if (Model is null) return;
        _input = new CostCenterInput
        {
            Code = Model.Code, NameAr = Model.NameAr, NameEn = Model.NameEn,
            ParentId = Model.ParentId, IsGroup = Model.IsGroup, BranchId = Model.BranchId,
            Notes = Model.Notes, IsActive = Model.IsActive
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
                ? await CostCenters.UpdateAsync(actor, Model.Id, Model.Version, _input)
                : ParentId is null
                    ? await CostCenters.CreateRootAsync(actor, _input)
                    : await CostCenters.CreateChildAsync(actor, ParentId.Value, _input);
            MudDialog.Close(DialogResult.Ok(saved));
        }
        catch (AppException ex) { Snackbar.Add(T.T(ex.Code), Severity.Error); }
        finally { _busy = false; }
    }
}
