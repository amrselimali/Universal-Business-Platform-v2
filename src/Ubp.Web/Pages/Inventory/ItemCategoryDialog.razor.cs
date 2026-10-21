using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Web.Services;

namespace Ubp.Web.Pages.Inventory;

public partial class ItemCategoryDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;
    [Parameter] public ItemCategory? Model { get; set; }
    [Inject] private ItemCategoryService Categories { get; set; } = default!;
    [Inject] private UserContext UserContext { get; set; } = default!;
    [Inject] private UiCulture T { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private MudForm _form = default!;
    private ItemCategoryInput _input = new();
    private IReadOnlyList<ItemCategory> _options = Array.Empty<ItemCategory>();
    private bool _busy;
    private bool IsEdit => Model is not null;

    protected override async Task OnInitializedAsync()
    {
        if (Model is not null)
        {
            _input = new ItemCategoryInput
            {
                Code = Model.Code, NameAr = Model.NameAr, NameEn = Model.NameEn,
                ParentId = Model.ParentId, IsActive = Model.IsActive
            };
        }
        var actor = await UserContext.RequireAsync();
        _options = await Categories.OptionsAsync(actor);
    }

    private string Label(ItemCategory category) =>
        T.Language == "en" && !string.IsNullOrWhiteSpace(category.NameEn) ? category.NameEn! : category.NameAr;

    private void Cancel() => MudDialog.Cancel();

    private async Task SaveAsync()
    {
        await _form.Validate();
        if (!_form.IsValid) return;
        _busy = true;
        try
        {
            var actor = await UserContext.RequireAsync();
            var saved = Model is null
                ? await Categories.CreateAsync(actor, _input)
                : await Categories.UpdateAsync(actor, Model.Id, Model.Version, _input);
            MudDialog.Close(DialogResult.Ok(saved));
        }
        catch (AppException ex) { Snackbar.Add(T.T(ex.Code), Severity.Error); }
        finally { _busy = false; }
    }
}
