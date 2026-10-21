using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Web.Services;

namespace Ubp.Web.Pages.Partners;

public partial class PartnerDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;
    [Parameter] public PartnerKind Kind { get; set; } = PartnerKind.Customer;
    [Parameter] public Partner? Model { get; set; }
    [Inject] private PartnerService Partners { get; set; } = default!;
    [Inject] private BranchService Branches { get; set; } = default!;
    [Inject] private UserContext UserContext { get; set; } = default!;
    [Inject] private UiCulture T { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private MudForm _form = default!;
    private PartnerInput _input = new();
    private IReadOnlyList<PartnerCategory> _categories = Array.Empty<PartnerCategory>();
    private IReadOnlyList<Branch> _branches = Array.Empty<Branch>();
    private bool _busy;
    private bool IsEdit => Model is not null;

    protected override async Task OnInitializedAsync()
    {
        var actor = await UserContext.RequireAsync();
        _categories = await Partners.ListCategoriesAsync(actor, Kind, false);
        _branches = await Branches.OptionsAsync(actor);

        if (Model is null)
        {
            if (actor.CurrentBranchId.HasValue)
            {
                _input.ScopeType = "BRANCH";
                _input.BranchId = actor.CurrentBranchId;
            }
            return;
        }

        _input = new PartnerInput
        {
            Code = Model.Code, NameAr = Model.NameAr, NameEn = Model.NameEn,
            CategoryId = Model.CategoryId, ScopeType = Model.ScopeType, BranchId = Model.BranchId,
            Phone = Model.Phone, Email = Model.Email, Address = Model.Address,
            TaxNumber = Model.TaxNumber, CreditLimit = Model.CreditLimit, Notes = Model.Notes,
            IsActive = Model.IsActive
        };
    }

    private string CategoryName(PartnerCategory category) =>
        T.Language == "en" && !string.IsNullOrWhiteSpace(category.NameEn) ? category.NameEn! : category.NameAr;

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
                ? await Partners.CreateAsync(actor, Kind, _input)
                : await Partners.UpdateAsync(actor, Kind, Model.Id, Model.Version, _input);
            MudDialog.Close(DialogResult.Ok(saved));
        }
        catch (AppException ex) { Snackbar.Add(T.T(ex.Code), Severity.Error); }
        finally { _busy = false; }
    }
}
