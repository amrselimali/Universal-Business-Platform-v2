using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Web.Services;

namespace Ubp.Web.Pages.Inventory;

public partial class ItemDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;
    [Parameter] public Item? Model { get; set; }
    [Inject] private ItemService Items { get; set; } = default!;
    [Inject] private UnitService Units { get; set; } = default!;
    [Inject] private ItemCategoryService Categories { get; set; } = default!;
    [Inject] private CustomFieldService CustomFields { get; set; } = default!;
    [Inject] private UserContext UserContext { get; set; } = default!;
    [Inject] private UiCulture T { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private MudForm _form = default!;
    private ItemInput _input = new();
    private IReadOnlyList<Unit> _units = Array.Empty<Unit>();
    private IReadOnlyList<ItemCategory> _categories = Array.Empty<ItemCategory>();
    private readonly List<ItemUnitInput> _unitRows = new();
    private readonly List<string> _barcodes = new();
    private IReadOnlyList<CustomFieldDefinition> _fields = Array.Empty<CustomFieldDefinition>();
    private readonly Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);
    private string? _barcode;
    private bool _busy;
    private bool IsEdit => Model is not null;

    protected override async Task OnInitializedAsync()
    {
        var actor = await UserContext.RequireAsync();
        _units = await Units.OptionsAsync(actor);
        _categories = await Categories.OptionsAsync(actor);
        _fields = (await CustomFields.ListActiveAsync("items")).ToList();

        if (Model is null)
        {
            if (_units.Count > 0) _input.BaseUnitId = _units[0].Id;
            return;
        }

        var detail = await Items.GetDetailAsync(actor, Model.Id);
        _input = new ItemInput
        {
            Code = detail.Item.Code, NameAr = detail.Item.NameAr, NameEn = detail.Item.NameEn,
            CategoryId = detail.Item.CategoryId, BaseUnitId = detail.Item.BaseUnitId,
            ItemType = detail.Item.ItemType, Notes = detail.Item.Notes, IsActive = detail.Item.IsActive
        };
        _unitRows.AddRange(detail.Units.Select(u => new ItemUnitInput
        {
            UnitId = u.UnitId, ConversionFactor = u.ConversionFactor,
            SalePrice = u.SalePrice, PurchasePrice = u.PurchasePrice, IsActive = u.IsActive
        }));
        _barcodes.AddRange(detail.Barcodes.Select(b => b.Barcode));
        foreach (var pair in detail.Item.CustomFields)
            _values[pair.Key] = pair.Value?.ToString();
    }

    private string Label(CustomFieldDefinition field) =>
        T.Language == "en" && !string.IsNullOrWhiteSpace(field.LabelEn) ? field.LabelEn! : field.LabelAr;

    private string? Get(string key) => _values.TryGetValue(key, out var value) ? value : null;
    private void Set(string key, string? value) => _values[key] = value;
    private bool GetBool(string key) => bool.TryParse(Get(key), out var value) && value;
    private void SetBool(string key, bool value) => _values[key] = value.ToString().ToLowerInvariant();

    private string UnitName(Unit unit) => T.Language == "en" && !string.IsNullOrWhiteSpace(unit.NameEn) ? unit.NameEn! : unit.NameAr;
    private string CategoryName(ItemCategory category) =>
        T.Language == "en" && !string.IsNullOrWhiteSpace(category.NameEn) ? category.NameEn! : category.NameAr;

    private void AddUnitRow() => _unitRows.Add(new ItemUnitInput
    {
        UnitId = _input.BaseUnitId,
        ConversionFactor = 1
    });

    private void AddBarcode()
    {
        var value = (_barcode ?? "").Trim();
        if (value.Length == 0 || _barcodes.Contains(value, StringComparer.OrdinalIgnoreCase)) return;
        _barcodes.Add(value);
        _barcode = null;
    }

    private void Cancel() => MudDialog.Cancel();

    private async Task SaveAsync()
    {
        await _form.Validate();
        if (!_form.IsValid) return;
        _busy = true;
        try
        {
            var actor = await UserContext.RequireAsync();
            _input.Units = _unitRows.ToList();
            _input.Barcodes = _barcodes.ToList();
            _input.CustomFields = _values.ToDictionary(p => p.Key, p => (object?)p.Value);
            var saved = Model is null
                ? await Items.CreateAsync(actor, _input)
                : await Items.UpdateAsync(actor, Model.Id, Model.Version, _input);
            MudDialog.Close(DialogResult.Ok(saved));
        }
        catch (AppException ex) { Snackbar.Add(T.T(ex.Code), Severity.Error); }
        finally { _busy = false; }
    }
}
