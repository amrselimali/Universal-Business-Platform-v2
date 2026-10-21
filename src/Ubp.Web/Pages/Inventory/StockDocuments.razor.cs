using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Security;

namespace Ubp.Web.Pages.Inventory;

public partial class StockDocuments
{
    [Inject] private StockDocumentService Documents { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;

    private MudTable<StockDocument> _table = default!;
    private string? _search;
    private string? _type;
    private string? _status;

    protected override string ScreenCode => ScreenCodes.StockDocuments;
    protected override Task OnLoadAsync() => Task.CompletedTask;

    private static Color StatusColor(string status) => status switch
    {
        StockStatuses.Posted or StockStatuses.Received => Color.Success,
        StockStatuses.PartiallyReceived => Color.Info,
        StockStatuses.Cancelled => Color.Error,
        _ => Color.Default
    };

    private async Task<TableData<StockDocument>> LoadServerData(TableState state, CancellationToken ct)
    {
        var result = await Documents.ListAsync(Actor!, new PageRequest
        {
            Page = state.Page + 1, PageSize = 25, Search = _search,
            SortBy = state.SortLabel ?? "doc_date",
            Descending = state.SortLabel is null || state.SortDirection == SortDirection.Descending
        }, new StockDocumentFilter { DocType = _type, Status = _status }, ct);
        return new TableData<StockDocument> { Items = result.Items, TotalItems = result.Total };
    }

    private async Task ReloadAsync() => await _table.ReloadServerData();

    private async Task AddAsync()
    {
        var dialog = await DialogService.ShowAsync<StockDocumentDialog>(T.T("stock.add"),
            new DialogParameters(), new DialogOptions { MaxWidth = MaxWidth.ExtraLarge, FullWidth = true });
        if ((await dialog.Result) is { Canceled: false }) { await ReloadAsync(); ShowSuccess("stock.saved"); }
    }

    private async Task EditAsync(StockDocument document)
    {
        var dialog = await DialogService.ShowAsync<StockDocumentDialog>(T.T("stock.edit"),
            new DialogParameters { ["Model"] = document },
            new DialogOptions { MaxWidth = MaxWidth.ExtraLarge, FullWidth = true });
        if ((await dialog.Result) is { Canceled: false }) { await ReloadAsync(); ShowSuccess("stock.saved"); }
    }

    private async Task PostAsync(StockDocument document)
    {
        var confirmed = await DialogService.ShowMessageBoxAsync(T.T("stock.post"),
            T.T("stock.post_confirm", document.DocNo ?? document.DocType),
            yesText: T.T("stock.post"), cancelText: T.T("common.cancel"));
        if (confirmed != true) return;
        await RunAsync(async () =>
        {
            await Documents.PostAsync(Actor!, document.Id);
            await ReloadAsync();
            ShowSuccess("stock.posted");
        });
    }

    private async Task ReverseAsync(StockDocument document)
    {
        var confirmed = await DialogService.ShowMessageBoxAsync(T.T("stock.reverse"),
            T.T("stock.reverse_confirm"), yesText: T.T("stock.reverse"), cancelText: T.T("common.cancel"));
        if (confirmed != true) return;
        await RunAsync(async () =>
        {
            await Documents.ReverseAsync(Actor!, document.Id);
            await ReloadAsync();
            ShowSuccess("stock.reversed");
        });
    }

    private async Task ReceiveAsync(StockDocument document)
    {
        var dialog = await DialogService.ShowAsync<ReceiveTransferDialog>(T.T("stock.receive_title"),
            new DialogParameters { ["DocumentId"] = document.Id },
            new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true });
        if ((await dialog.Result) is { Canceled: false }) { await ReloadAsync(); ShowSuccess("stock.received_ok"); }
    }

    private async Task CancelAsync(StockDocument document)
    {
        var confirmed = await DialogService.ShowMessageBoxAsync(T.T("stock.cancel"), T.T("stock.cancel_confirm"),
            yesText: T.T("stock.cancel"), cancelText: T.T("common.cancel"));
        if (confirmed != true) return;
        await RunAsync(async () =>
        {
            await Documents.CancelAsync(Actor!, document.Id);
            await ReloadAsync();
            ShowSuccess("common.saved");
        });
    }

    private async Task DeleteAsync(StockDocument document)
    {
        var confirmed = await DialogService.ShowMessageBoxAsync(T.T("common.confirm_delete"),
            document.DocNo ?? document.DocType, yesText: T.T("common.delete"), cancelText: T.T("common.cancel"));
        if (confirmed != true) return;
        await RunAsync(async () =>
        {
            await Documents.DeleteAsync(Actor!, document.Id);
            await ReloadAsync();
            ShowSuccess("common.deleted");
        });
    }
}
