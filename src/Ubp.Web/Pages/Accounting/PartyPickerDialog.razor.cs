using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Web.Services;

namespace Ubp.Web.Pages.Accounting;

public partial class PartyPickerDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;
    [Parameter] public PartnerKind Kind { get; set; } = PartnerKind.Customer;

    [Inject] private LookupService Lookups { get; set; } = default!;
    [Inject] private UserContext UserContext { get; set; } = default!;

    private string? _search;
    private IReadOnlyList<Partner> _results = Array.Empty<Partner>();
    private bool _loaded;

    protected override async Task OnInitializedAsync() => await SearchAsync();

    private async Task SearchAsync()
    {
        var actor = await UserContext.RequireAsync();
        _results = await Lookups.PartnersAsync(actor, Kind, _search);
        _loaded = true;
    }

    private void Select(Partner partner) =>
        MudDialog.Close(DialogResult.Ok(new PartyOption
        {
            Id = partner.Id, Code = partner.Code, Name = partner.NameAr, Phone = partner.Phone
        }));
}
