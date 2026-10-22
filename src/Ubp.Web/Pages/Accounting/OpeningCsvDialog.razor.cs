using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Web.Services;

namespace Ubp.Web.Pages.Accounting;

public partial class OpeningCsvDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;

    [Inject] private OpeningBalanceService OpeningService { get; set; } = default!;
    [Inject] private UserContext UserContext { get; set; } = default!;
    [Inject] private UiCulture T { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private string? _csv;
    private OpeningBalanceImportResult? _result;
    private bool _busy;

    private async Task ParseAsync()
    {
        _busy = true;
        try
        {
            var actor = await UserContext.RequireAsync();
            _result = await OpeningService.ImportCsvAsync(actor, _csv ?? "");
        }
        catch (AppException ex) { Snackbar.Add(T.T(ex.Code), Severity.Error); }
        finally { _busy = false; }
    }

    private void Confirm() => MudDialog.Close(DialogResult.Ok(_result));
}
