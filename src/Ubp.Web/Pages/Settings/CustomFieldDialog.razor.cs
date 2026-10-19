using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Web.Services;

namespace Ubp.Web.Pages.Settings;

public partial class CustomFieldDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;
    [Parameter] public CustomFieldDefinition? Model { get; set; }
    [Inject] private CustomFieldService CustomFields { get; set; } = default!;
    [Inject] private UserContext UserContext { get; set; } = default!;
    [Inject] private UiCulture T { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private MudForm _form = default!;
    private CustomFieldInput _input = new() { EntityName = "branches", DataType = "text" };
    private string _options = "";
    private int _sortOrder;
    private bool _busy;

    private bool IsEdit => Model is not null;

    protected override void OnInitialized()
    {
        if (Model is not null)
        {
            _input = new CustomFieldInput
            {
                EntityName = Model.EntityName, FieldKey = Model.FieldKey, LabelAr = Model.LabelAr,
                LabelEn = Model.LabelEn, DataType = Model.DataType, Options = Model.Options,
                IsRequired = Model.IsRequired, IsActive = Model.IsActive, SortOrder = Model.SortOrder
            };
            _options = string.Join(", ", Model.Options);
            _sortOrder = Model.SortOrder;
        }
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
            _input.Options = _options.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            _input.SortOrder = _sortOrder;
            CustomFieldDefinition saved = Model is null
                ? await CustomFields.CreateAsync(actor, _input)
                : await CustomFields.UpdateAsync(actor, Model.Id, Model.Version, _input);
            MudDialog.Close(DialogResult.Ok(saved));
        }
        catch (AppException ex)
        {
            Snackbar.Add(T.T(ex.Code), Severity.Error);
        }
        finally { _busy = false; }
    }
}
