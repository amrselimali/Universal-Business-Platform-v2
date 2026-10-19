using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Web.Services;

namespace Ubp.Web.Pages.Settings;

public partial class WarehouseDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;
    [Parameter] public Warehouse? Model { get; set; }
    [Inject] private WarehouseService Warehouses { get; set; } = default!;
    [Inject] private BranchService Branches { get; set; } = default!;
    [Inject] private CustomFieldService CustomFields { get; set; } = default!;
    [Inject] private UserContext UserContext { get; set; } = default!;
    [Inject] private UiCulture T { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private MudForm _form = default!;
    private WarehouseInput _input = new();
    private List<Branch> _branches = new();
    private List<CustomFieldDefinition> _fields = new();
    private IReadOnlyCollection<Guid> _selectedBranches = new List<Guid>();
    private readonly Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);
    private bool _busy;

    private bool IsEdit => Model is not null;

    protected override async Task OnInitializedAsync()
    {
        var actor = await UserContext.RequireAsync();
        _branches = (await Branches.OptionsAsync(actor)).ToList();
        _fields = (await CustomFields.ListActiveAsync("warehouses")).ToList();

        if (Model is not null)
        {
            _input = new WarehouseInput
            {
                Code = Model.Code, NameAr = Model.NameAr, NameEn = Model.NameEn,
                IsActive = Model.IsActive, CustomFields = Model.CustomFields
            };
            _selectedBranches = Model.BranchIds.ToList();
            foreach (var pair in Model.CustomFields)
                _values[pair.Key] = pair.Value?.ToString();
        }
    }

    private string Label(CustomFieldDefinition field) =>
        T.Language == "en" && !string.IsNullOrWhiteSpace(field.LabelEn) ? field.LabelEn! : field.LabelAr;

    private string? Get(string key) => _values.TryGetValue(key, out var value) ? value : null;
    private void Set(string key, string? value) => _values[key] = value;
    private bool GetBool(string key) => bool.TryParse(Get(key), out var value) && value;
    private void SetBool(string key, bool value) => _values[key] = value.ToString().ToLowerInvariant();
    private void Cancel() => MudDialog.Cancel();

    private async Task SaveAsync()
    {
        await _form.Validate();
        if (!_form.IsValid) return;

        _busy = true;
        try
        {
            var actor = await UserContext.RequireAsync();
            _input.CustomFields = _values.ToDictionary(p => p.Key, p => (object?)p.Value);
            _input.BranchIds = _selectedBranches.ToList();
            Warehouse saved = Model is null
                ? await Warehouses.CreateAsync(actor, _input)
                : await Warehouses.UpdateAsync(actor, Model.Id, Model.Version, _input);
            MudDialog.Close(DialogResult.Ok(saved));
        }
        catch (AppException ex)
        {
            Snackbar.Add(T.T(ex.Code), Severity.Error);
        }
        finally { _busy = false; }
    }
}
