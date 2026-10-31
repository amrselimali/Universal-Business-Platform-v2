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
    private MudTable<ReservationRow> _table = default!;
    private ReservationRow? _selected;
    private bool _onlyRemaining = true;
    private bool _busy;
    private DateTime? _extendTo = DateTime.Today.AddDays(30);

    protected override string ScreenCode => ScreenCodes.Reservations;

    protected override Task OnLoadAsync() => Task.CompletedTask;

    private async Task<TableData<ReservationRow>> LoadAsync(TableState state, CancellationToken ct)
    {
        var page = await ReservationsApi.ListAsync(Actor!, new PageRequest
        {
            Page = state.Page + 1,
            PageSize = state.PageSize <= 25 ? 25 : 50
        }, null, _onlyRemaining, ct);
        return new TableData<ReservationRow> { Items = page.Items, TotalItems = page.Total };
    }

    private async Task RefreshAsync() => await _table.ReloadServerData();

    private async Task OnRemainingChanged(bool value)
    {
        _onlyRemaining = value;
        await RefreshAsync();
    }

    private void OnSelected(ReservationRow? row) => _selected = row;

    private async Task ConvertAsync(ReservationRow row)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var result = await ReservationsApi.ConvertToAdvanceAsync(Actor!, row.BookingId);
            Snackbar.Add(T.T("reservations.converted") + $" ({result.LinesConverted})", Severity.Success);
            await RefreshAsync();
        }
        catch (AppException ex) { Snackbar.Add(T.T(ex.Code), Severity.Error); }
        finally { _busy = false; }
    }

    private async Task ExtendAsync(ReservationRow row)
    {
        if (_busy || _extendTo is null) return;
        _busy = true;
        try
        {
            await ReservationsApi.ExtendAsync(Actor!, row.BookingId,
                new DateTimeOffset(_extendTo.Value));
            Snackbar.Add(T.T("reservations.extended"), Severity.Success);
            await RefreshAsync();
        }
        catch (AppException ex) { Snackbar.Add(T.T(ex.Code), Severity.Error); }
        finally { _busy = false; }
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
