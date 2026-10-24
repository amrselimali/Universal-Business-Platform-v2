using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Web.Pages.Accounting;

public partial class PartyPickerDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;
    [Parameter] public PartnerKind Kind { get; set; } = PartnerKind.Customer;

    [Inject] private IPartnerRepository Partners { get; set; } = default!;

    private string? _search;
    private IReadOnlyList<Partner> _results = Array.Empty<Partner>();
    private bool _loaded;

    protected override async Task OnInitializedAsync() => await SearchAsync();

    private async Task SearchAsync()
    {
        var page = await Partners.ListAsync(Kind, new PageRequest
        {
            Page = 1, PageSize = PageRequest.MaxPageSize, Search = _search
        }, null, false, Array.Empty<Guid>());
        _results = page.Items;
        _loaded = true;
    }

    private void Select(Partner partner) =>
        MudDialog.Close(DialogResult.Ok(new PartyOption
        {
            Id = partner.Id, Code = partner.Code, Name = partner.NameAr, Phone = partner.Phone
        }));
}
