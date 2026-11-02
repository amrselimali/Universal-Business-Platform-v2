using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Results;
using Ubp.Core.Security;

namespace Ubp.Web.Pages.Sales;

public partial class Reservations
{
    [Inject] private IDialogService Dialogs { get; set; } = default!;
    private MudTable<ReservationRow> _table = default!;
    private ReservationRow? _selected;
    private bool _onlyRemaining = true;
    private bool _busy;
    private string? _customerSearch;

    protected override string ScreenCode => ScreenCodes.Reservations;

    protected override Task OnLoadAsync() => Task.CompletedTask;

    private async Task<TableData<ReservationRow>> LoadAsync(TableState state, CancellationToken ct)
    {
        var page = await ReservationsApi.ListAsync(Actor!, new PageRequest
        {
            Page = state.Page + 1,
            PageSize = state.PageSize <= 25 ? 25 : 50
        }, null, _onlyRemaining, _customerSearch, ct);
        return new TableData<ReservationRow> { Items = page.Items, TotalItems = page.Total };
    }

    private async Task RefreshAsync() => await _table.ReloadServerData();

    private async Task OnRemainingChanged(bool value)
    {
        _onlyRemaining = value;
        await RefreshAsync();
    }

    private void OnSelected(ReservationRow? row) => _selected = row;

    private async Task ExecuteAsync(ReservationRow row)
    {
        var dialog = await Dialogs.ShowAsync<ExecutionDialog>(T.T("execution.title"),
            new DialogParameters { ["BookingLineId"] = row.BookingLineId });
        if ((await dialog.Result) is { Canceled: false })
        {
            await RefreshAsync();
            Snackbar.Add(T.T("execution.done"), Severity.Success);
        }
    }

    private async Task ReleaseAsync(ReservationRow row)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            await ReservationsApi.ReleaseAsync(Actor!, row.BookingId, T.T("reservations.release_reason"));
            Snackbar.Add(T.T("reservations.released"), Severity.Success);
            await RefreshAsync();
        }
        catch (AppException ex) { Snackbar.Add(T.T(ex.Code), Severity.Error); }
        finally { _busy = false; }
    }
}
