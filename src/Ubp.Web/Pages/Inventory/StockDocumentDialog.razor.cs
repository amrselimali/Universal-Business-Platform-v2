using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Web.Services;

namespace Ubp.Web.Pages.Inventory;

public partial class StockDocumentDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;
    [Parameter] public StockDocument? Model { get; set; }
    [Inject] private StockDocumentService Documents { get; set; } = default!;
    [Inject] private BranchService Branches { get; set; } = default!;
    [Inject] private WarehouseService Warehouses { get; set; } = default!;
    [Inject] private ItemService Items { get; set; } = default!;
    [Inject] private UserContext UserContext { get; set; } = default!;
    [Inject] private UiCulture T { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private MudForm _form = default!;
    private string _docType = StockDocumentTypes.Receipt;
    private DateTime? _date = DateTime.Today;
    private Guid _branchId;
    private Guid _warehouseId;
    private Guid? _toBranchId;
    private Guid? _toWarehouseId;
    private string? _notes;
    private IReadOnlyList<Branch> _branches = Array.Empty<Branch>();
    private IReadOnlyList<Warehouse> _warehouses = Array.Empty<Warehouse>();
    private readonly List<LineModel> _lines = new();
    private bool _busy;
    private bool IsEdit => Model is not null;

    private IEnumerable<Warehouse> WarehouseOptions =>
        _warehouses.Where(w => w.BranchIds.Contains(_branchId));

    protected override async Task OnInitializedAsync()
    {
        var actor = await UserContext.RequireAsync();
        _branches = await Branches.OptionsAsync(actor);
        _warehouses = await Warehouses.OptionsAsync(actor);

        if (Model is null)
        {
            _branchId = actor.CurrentBranchId ?? _branches.FirstOrDefault()?.Id ?? Guid.Empty;
            _warehouseId = WarehouseOptions.FirstOrDefault()?.Id ?? Guid.Empty;
            _lines.Add(new LineModel());
            return;
        }

        var detail = await Documents.GetDetailAsync(actor, Model.Id);
        _docType = detail.Document.DocType;
        _date = detail.Document.DocDate;
        _branchId = detail.Document.BranchId;
        _warehouseId = detail.Document.WarehouseId;
        _toBranchId = detail.Document.ToBranchId;
        _toWarehouseId = detail.Document.ToWarehouseId;
        _notes = detail.Document.Notes;

        foreach (var line in detail.Lines)
        {
            var itemDetail = await Items.GetDetailAsync(actor, line.ItemId);
            _lines.Add(new LineModel
            {
                Item = new ItemOption
                {
                    Id = itemDetail.Item.Id, Code = itemDetail.Item.Code,
                    NameAr = itemDetail.Item.NameAr, NameEn = itemDetail.Item.NameEn,
                    BaseUnitId = itemDetail.Item.BaseUnitId, BaseUnitName = itemDetail.Item.BaseUnitName
                },
                Units = itemDetail.Units,
                UnitId = line.UnitId,
                Quantity = line.Quantity,
                UnitCost = line.UnitCost,
                Direction = line.Direction,
                Notes = line.Notes
            });
        }
    }

    private void OnBranchChanged()
    {
        if (!WarehouseOptions.Any(w => w.Id == _warehouseId))
            _warehouseId = WarehouseOptions.FirstOrDefault()?.Id ?? Guid.Empty;
        _toBranchId = null;
        _toWarehouseId = null;
    }

    private async Task<IEnumerable<ItemOption>> SearchItems(string? search, CancellationToken ct) =>
        await Items.OptionsAsync(await UserContext.RequireAsync(), search);

    private string ItemLabel(ItemOption? item) => item is null ? "" :
        (T.Language == "en" && !string.IsNullOrWhiteSpace(item.NameAr)
            ? $"{item.Code} — {item.NameEn ?? item.NameAr}"
            : $"{item.Code} — {item.NameAr}");

    private async Task OnItemChangedAsync(LineModel line, ItemOption? item)
    {
        line.Item = item;
        if (item is null) { line.Units = Array.Empty<ItemUnit>(); return; }
        var detail = await Items.GetDetailAsync(await UserContext.RequireAsync(), item.Id);
        line.Units = detail.Units;
        line.UnitId = detail.Item.BaseUnitId;
        if (line.UnitCost == 0)
            line.UnitCost = detail.Units.FirstOrDefault(u => u.UnitId == detail.Item.BaseUnitId)?.PurchasePrice ?? 0;
    }

    private void AddLine() => _lines.Add(new LineModel());
    private void Cancel() => MudDialog.Cancel();

    private async Task SaveAsync()
    {
        await _form.Validate();
        if (!_form.IsValid) return;
        _busy = true;
        try
        {
            var actor = await UserContext.RequireAsync();
            var input = new StockDocumentInput
            {
                DocType = _docType,
                DocDate = _date ?? DateTime.Today,
                BranchId = _branchId,
                WarehouseId = _warehouseId,
                ToBranchId = _docType == StockDocumentTypes.Transfer ? _toBranchId : null,
                ToWarehouseId = _docType == StockDocumentTypes.Transfer ? _toWarehouseId : null,
                Notes = _notes,
                Lines = _lines.Where(l => l.Item is not null && l.Quantity > 0).Select(l => new StockDocumentLineInput
                {
                    ItemId = l.Item!.Id,
                    UnitId = l.UnitId,
                    ConversionFactor = l.Units.FirstOrDefault(u => u.UnitId == l.UnitId)?.ConversionFactor ?? 1,
                    Quantity = l.Quantity,
                    UnitCost = l.UnitCost,
                    LotNumber = l.LotNumber,
                    ExpiryDate = l.ExpiryDate,
                    Direction = l.Direction,
                    Notes = l.Notes
                }).ToList()
            };
            var saved = await Documents.SaveAsync(actor, Model?.Id, Model?.Version ?? 0, input);
            MudDialog.Close(DialogResult.Ok(saved));
        }
        catch (AppException ex) { Snackbar.Add(T.T(ex.Code), Severity.Error); }
        finally { _busy = false; }
    }

    /// <summary>حقول الدفعة والصلاحية تظهر للمستندات الداخلة فقط (D16).</summary>
    private bool ShowsLotFields => _docType is StockDocumentTypes.Receipt
        or StockDocumentTypes.Opening;

    public sealed class LineModel
    {
        public ItemOption? Item { get; set; }
        public Guid UnitId { get; set; }
        public IReadOnlyList<ItemUnit> Units { get; set; } = Array.Empty<ItemUnit>();
        public decimal Quantity { get; set; } = 1;
        public decimal UnitCost { get; set; }
        public string? LotNumber { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public string Direction { get; set; } = "IN";
        public string? Notes { get; set; }
    }
}
