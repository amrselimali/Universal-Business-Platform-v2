using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Core.Models;
using Ubp.Web.Services;

namespace Ubp.Web.Pages.Accounting;

public partial class StockPostingPreviewDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;
    [Parameter] public StockPostingPreview Preview { get; set; } = new();

    [Inject] private Application.Services.AccountService Accounts { get; set; } = default!;
    [Inject] private UserContext UserContext { get; set; } = default!;
    [Inject] private UiCulture T { get; set; } = default!;

    private IReadOnlyList<Account> _accounts = Array.Empty<Account>();
    private bool _english;

    protected override async Task OnInitializedAsync()
    {
        _english = T.Language == "en";
        var actor = await UserContext.GetAsync();
        if (actor is not null) _accounts = await Accounts.PostableAsync(actor);
    }

    private string AccountLabel(Guid accountId)
    {
        var account = _accounts.FirstOrDefault(a => a.Id == accountId);
        return account is null ? accountId.ToString() : $"{account.Code} — {account.DisplayName(_english)}";
    }
}
