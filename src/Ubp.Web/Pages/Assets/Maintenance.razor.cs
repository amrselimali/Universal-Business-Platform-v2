using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Core.Models;

namespace Ubp.Web.Pages.Assets;

public partial class Maintenance
{
    private IReadOnlyList<FixedAsset> _assets = Array.Empty<FixedAsset>();
    private IReadOnlyList<MaintenanceOrder> _orders = Array.Empty<MaintenanceOrder>();
    private IReadOnlyList<MaintenancePlan> _plans = Array.Empty<MaintenancePlan>();
    private Guid? _assetId;
    private bool _busy;
    private bool _showOrderForm;
    private bool _showPlanForm;

    private DateTime? _orderDate = DateTime.Today;
    private decimal _orderCost;
    private string _orderAction = "PREVENTIVE";

    private string _planKind = "TIME";
    private int? _intervalDays = 90;
    private decimal? _intervalUnits;

    protected override string ScreenCode => ScreenCodes.AssetMaintenance;

    protected override async Task OnLoadAsync()
    {
        var page = await AssetsApi.ListAsync(Actor!, new PageRequest { PageSize = PageRequest.MaxPageSize });
        _assets = page.Items;
        _assetId = _assets.FirstOrDefault()?.Id;
        await ReloadAsync();
    }

    private async Task ReloadAsync() => await RunAsync(async () =>
    {
        if (_assetId is null)
        {
            _orders = Array.Empty<MaintenanceOrder>();
            _plans = Array.Empty<MaintenancePlan>();
            return;
        }
        _orders = await AssetsApi.OrdersAsync(Actor!, _assetId.Value);
        _plans = await AssetsApi.PlansAsync(Actor!, _assetId.Value);
    });

    private async Task OnAssetChanged(Guid? assetId)
    {
        _assetId = assetId;
        await ReloadAsync();
    }

    private async Task OpenOrderAsync()
    {
        if (_assetId is null || _busy) return;
        _busy = true;
        try
        {
            await AssetsApi.OpenOrderAsync(Actor!, new MaintenanceOrderInput(_assetId.Value, null,
                _orderDate ?? DateTime.Today, _orderCost, _orderAction, null));
            _showOrderForm = false;
            _orderCost = 0;
            await ReloadAsync();
            Snackbar.Add(T.T("assets.order_opened"), Severity.Success);
        }
        catch (AppException ex) { Snackbar.Add(T.T(ex.Code), Severity.Error); }
        finally { _busy = false; }
    }

    private async Task CloseOrderAsync(MaintenanceOrder order)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            await AssetsApi.CloseOrderAsync(Actor!, order.Id);
            await ReloadAsync();
            Snackbar.Add(T.T("assets.order_closed"), Severity.Success);
        }
        catch (AppException ex) { Snackbar.Add(T.T(ex.Code), Severity.Error); }
        finally { _busy = false; }
    }

    private async Task CreatePlanAsync()
    {
        if (_assetId is null || _busy) return;
        _busy = true;
        try
        {
            await AssetsApi.CreatePlanAsync(Actor!, new MaintenancePlanInput(_assetId.Value, _planKind,
                _planKind == "TIME" ? _intervalDays : null,
                _planKind == "METER" ? _intervalUnits : null, null, null));
            _showPlanForm = false;
            await ReloadAsync();
            Snackbar.Add(T.T("assets.plan_saved"), Severity.Success);
        }
        catch (AppException ex) { Snackbar.Add(T.T(ex.Code), Severity.Error); }
        finally { _busy = false; }
    }
}
