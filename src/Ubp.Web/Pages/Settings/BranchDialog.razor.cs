using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Web.Services;

namespace Ubp.Web.Pages.Settings;

public partial class BranchDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;
    [Parameter] public Branch? Model { get; set; }
    [Inject] private BranchService Branches { get; set; } = default!;
    [Inject] private CustomFieldService CustomFields { get; set; } = default!;
    [Inject] private UserContext UserContext { get; set; } = default!;
    [Inject] private UiCulture T { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private MudForm _form = default!;
    private BranchInput _input = new();
    private List<CustomFieldDefinition> _fields = new();
    private readonly Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);
    private bool _busy;

    private bool IsEdit => Model is not null;

    protected override async Task OnInitializedAsync()
    {
        if (Model is not null)
        {
            _input = new BranchInput
            {
                Code = Model.Code, NameAr = Model.NameAr, NameEn = Model.NameEn,
                Address = Model.Address, Phone = Model.Phone, IsActive = Model.IsActive,
                CustomFields = Model.CustomFields
            };
            foreach (var pair in Model.CustomFields)
                _values[pair.Key] = pair.Value?.ToString();
        }
        _fields = (await CustomFields.ListActiveAsync("branches")).ToList();
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
            Branch saved = Model is null
                ? await Branches.CreateAsync(actor, _input)
                : await Branches.UpdateAsync(actor, Model.Id, Model.Version, _input);
            MudDialog.Close(DialogResult.Ok(saved));
        }
        catch (AppException ex)
        {
            Snackbar.Add(T.T(ex.Code), Severity.Error);
        }
        finally { _busy = false; }
    }
}
