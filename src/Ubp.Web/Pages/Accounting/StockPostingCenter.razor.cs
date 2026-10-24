using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;

namespace Ubp.Web.Pages.Accounting;

public partial class StockPostingCenter
{
    [Inject] private StockAccountingService StockAccounting { get; set; } = default!;
    [Inject] private BranchService BranchService { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;

    private IReadOnlyList<StockPostingCandidate> _candidates = Array.Empty<StockPostingCandidate>();
    private IReadOnlyList<Branch> _branches = Array.Empty<Branch>();
    private Guid? _branchId;
    private DateTime? _from = new DateTime(DateTime.Today.Year, 1, 1);
    private DateTime? _to = DateTime.Today;
    private bool _busy;

    protected override string ScreenCode => ScreenCodes.StockPostingCenter;

    protected override async Task OnLoadAsync()
    {
        _branches = await BranchService.OptionsAsync(Actor!);
        await ReloadAsync();
    }

    private async Task ReloadAsync() => await RunAsync(async () =>
    {
        _candidates = await StockAccounting.ListAsync(Actor!, _branchId,
            _from ?? DateTime.Today.AddYears(-1), _to ?? DateTime.Today);
    });

    private async Task PreviewAsync(StockPostingCandidate candidate) => await RunAsync(async () =>
    {
        var preview = await StockAccounting.PreviewAsync(Actor!, candidate.DocumentId);
        await DialogService.ShowAsync<StockPostingPreviewDialog>(T.T("accounting.preview"),
            new DialogParameters { ["Preview"] = preview });
    });

    private async Task GenerateAsync(StockPostingCandidate candidate) => await RunAsync(async () =>
    {
        _busy = true;
        try
        {
            await StockAccounting.GenerateAsync(Actor!, candidate.DocumentId);
            ShowSuccess("accounting.posted");
        }
        finally { _busy = false; }
        await ReloadAsync();
    });

    private async Task GenerateAllAsync()
    {
        var confirmed = await DialogService.ShowMessageBoxAsync(T.T("accounting.generate_all"),
            T.T("accounting.generate_all_confirm"), yesText: T.T("common.confirm"),
            cancelText: T.T("common.cancel"));
        if (confirmed != true) return;

        await RunAsync(async () =>
        {
            _busy = true;
            try
            {
                var created = await StockAccounting.GenerateAllAsync(Actor!, _branchId,
                    _from ?? DateTime.Today.AddYears(-1), _to ?? DateTime.Today);
                Snackbar.Add($"{T.T("accounting.generated")}: {created}", Severity.Success);
            }
            finally { _busy = false; }
            await ReloadAsync();
        });
    }

    private void OpenEntry(Guid entryId) =>
        Navigation.NavigateTo($"/accounting/entries/{entryId}");
}
