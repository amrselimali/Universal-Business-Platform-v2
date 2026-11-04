using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Web.Services;

namespace Ubp.Web.Pages.Assets;

public partial class MeterFollowUpDialog : ComponentBase
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;
    [Parameter] public Guid AssetId { get; set; }

    [Inject] private AssetService AssetsApi { get; set; } = default!;
    [Inject] private UserContext UserContext { get; set; } = default!;
    [Inject] private UiCulture T { get; set; } = default!;
    [Inject] private IDisplayFormatter Fmt { get; set; } = default!;
    [Inject] private IDialogService Dialogs { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private AssetMeterStatement? _statement;
    private IReadOnlyList<AssetMeterStatementRow> _rows = Array.Empty<AssetMeterStatementRow>();
    private IReadOnlyList<AssetMeterMovement> _movements = Array.Empty<AssetMeterMovement>();
    private string _mode = MeterStatementModes.Month;
    private DateTime? _fromMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime? _toMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime? _fromDay = DateTime.Today;
    private DateTime? _toDay = DateTime.Today;
    private DateTime? _singleDay = DateTime.Today;
    private bool _busy;

    protected override async Task OnInitializedAsync() => await LoadAsync();

    private void Close() => MudDialog.Close(DialogResult.Ok(false));

    private async Task OnModeChanged(string mode)
    {
        _mode = mode;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var from = _mode switch
        {
            MeterStatementModes.Month => _fromMonth ?? DateTime.Today,
            MeterStatementModes.Day => _fromDay ?? DateTime.Today,
            _ => _singleDay ?? DateTime.Today
        };
        var to = _mode switch
        {
            MeterStatementModes.Month => _toMonth ?? from,
            MeterStatementModes.Day => _toDay ?? from,
            _ => from
        };

        _busy = true;
        try
        {
            var actor = await UserContext.RequireAsync();
            _statement = await AssetsApi.MeterStatementAsync(actor, AssetId, _mode, from, to);
            _rows = _statement.Rows;
            _movements = _statement.Movements;
        }
        catch (AppException ex) { Snackbar.Add(T.T(ex.Code), Severity.Error); }
        finally { _busy = false; }
    }

    /// <summary>إضافة تسوية للعدّاد بتاريخ السطر الافتراضي (بداية الشهر أو يوم السطر).</summary>
    private async Task AddAdjustmentAsync(AssetMeterStatementRow row)
    {
        var dialog = await Dialogs.ShowAsync<MeterAdjustmentDialog>(
            T.T("assets.meter_adjustment"), new DialogParameters
            {
                ["AssetId"] = AssetId,
                ["DefaultDate"] = row.Start.Date
            }, new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true });

        if ((await dialog.Result) is { Canceled: false })
        {
            Snackbar.Add(T.T("common.saved"), Severity.Success);
            await LoadAsync();
        }
    }
}
