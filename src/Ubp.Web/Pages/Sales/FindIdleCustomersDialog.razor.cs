using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Web.Services;

namespace Ubp.Web.Pages.Sales;

public partial class FindIdleCustomersDialog : ComponentBase
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;
    [Parameter] public IReadOnlyList<Branch> Branches { get; set; } = Array.Empty<Branch>();
    [Parameter] public Guid? DefaultBranchId { get; set; }

    [Inject] private FollowUpService FollowUps { get; set; } = default!;
    [Inject] private UserContext UserContext { get; set; } = default!;
    [Inject] private UiCulture T { get; set; } = default!;
    [Inject] private IDialogService Dialogs { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private string? _codeFrom;
    private string? _codeTo;
    private Guid? _branchId;
    private IReadOnlyList<CustomerGapRow> _rows = Array.Empty<CustomerGapRow>();
    private bool _busy;
    private bool _searched;

    protected override void OnInitialized() => _branchId = DefaultBranchId;

    private void Close() => MudDialog.Close(DialogResult.Ok(_searched));

    private async Task SearchAsync()
    {
        _busy = true;
        try
        {
            var actor = await UserContext.RequireAsync();
            _rows = await FollowUps.FindIdleCustomersAsync(actor, _codeFrom, _codeTo, _branchId);
            _searched = true;
        }
        catch (AppException ex) { Snackbar.Add(T.T(ex.Code), Severity.Error); }
        finally { _busy = false; }
    }

    /// <summary>آخر نشاط مسجّل (حجز/موعد/متابعة) في خلية واحدة.</summary>
    private string Summary(CustomerGapRow row)
    {
        var parts = new List<string>();
        if (row.LastBookingAt is { } booked)
            parts.Add($"{T.T("sales.last_booking")}: {booked.ToLocalTime():yyyy-MM-dd}" +
                      (string.IsNullOrWhiteSpace(row.LastBookingNo) ? "" : $" ({row.LastBookingNo})"));
        if (row.LastAppointmentAt is { } appointment)
            parts.Add($"{T.T("sales.last_appointment")}: {appointment.ToLocalTime():yyyy-MM-dd}");
        if (row.LastFollowUpAt is { } followUp)
            parts.Add($"{T.T("sales.last_followup")}: {followUp.ToLocalTime():yyyy-MM-dd}" +
                      $" ({T.T("sales.channel_" + row.LastFollowUpChannel)})" +
                      $" [{T.T("sales.followup_" + row.LastFollowUpStatus)}]");
        return parts.Count == 0 ? T.T("sales.no_activity") : string.Join(" · ", parts);
    }

    private async Task BookAsync(CustomerGapRow row)
    {
        var dialog = await Dialogs.ShowAsync<CustomerSessionDialog>(T.T("sales.session_title"),
            new DialogParameters
            {
                ["Branches"] = Branches,
                ["DefaultBranchId"] = _branchId ?? DefaultBranchId,
                ["DefaultCustomerId"] = row.CustomerId,
                ["DefaultCustomerName"] = $"{row.Code} — {row.NameAr}"
            });
        if ((await dialog.Result) is { Canceled: false })
        {
            Snackbar.Add(T.T("sales.session_saved"), Severity.Success);
            await SearchAsync();
        }
    }

    private async Task FollowUpAsync(CustomerGapRow row)
    {
        var dialog = await Dialogs.ShowAsync<QuickFollowUpDialog>(T.T("sales.followup_add"),
            new DialogParameters
            {
                ["CustomerId"] = row.CustomerId,
                ["CustomerName"] = $"{row.Code} — {row.NameAr}",
                ["DefaultDate"] = DateTime.Today
            });
        if ((await dialog.Result) is { Canceled: false })
        {
            Snackbar.Add(T.T("common.saved"), Severity.Success);
            await SearchAsync();
        }
    }
}
