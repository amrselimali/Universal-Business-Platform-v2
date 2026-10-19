using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Security;

namespace Ubp.Web.Pages.Settings;

public partial class CustomFields
{
    [Inject] private CustomFieldService CustomFieldsService { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;

    private MudTable<CustomFieldDefinition> _table = default!;
    private string? _entity;
    private string _entityLabel = "";

    protected override string ScreenCode => ScreenCodes.CustomFields;

    protected override Task OnLoadAsync() => Task.CompletedTask;

    private async Task<TableData<CustomFieldDefinition>> LoadServerData(TableState state, CancellationToken ct)
    {
        var request = new PageRequest { Page = state.Page + 1, PageSize = 25, Search = null };
        var result = await CustomFieldsService.ListAsync(Actor!, request, _entity, ct);
        _entityLabel = _entity ?? "";
        return new TableData<CustomFieldDefinition> { Items = result.Items, TotalItems = result.Total };
    }

    private async Task OnEntityChanged(string? value)
    {
        _entity = string.IsNullOrWhiteSpace(value) ? null : value;
        await _table.ReloadServerData();
    }

    private async Task AddAsync()
    {
        var dialog = await DialogService.ShowAsync<CustomFieldDialog>(T.T("custom_fields.add"), new DialogParameters());
        if ((await dialog.Result) is { Canceled: false })
        {
            await _table.ReloadServerData();
            ShowSuccess("custom_fields.saved");
        }
    }

    private async Task EditAsync(CustomFieldDefinition definition)
    {
        var dialog = await DialogService.ShowAsync<CustomFieldDialog>(T.T("custom_fields.edit"),
            new DialogParameters { ["Model"] = definition });
        if ((await dialog.Result) is { Canceled: false })
        {
            await _table.ReloadServerData();
            ShowSuccess("custom_fields.saved");
        }
    }

    private async Task DeleteAsync(CustomFieldDefinition definition)
    {
        var confirmed = await DialogService.ShowMessageBoxAsync(T.T("common.confirm_delete"),
            T.T("custom_fields.delete_confirm", definition.LabelAr), yesText: T.T("common.delete"),
            cancelText: T.T("common.cancel"));
        if (confirmed != true) return;
        await RunAsync(async () =>
        {
            await CustomFieldsService.DeleteAsync(Actor!, definition.Id);
            await _table.ReloadServerData();
            ShowSuccess("custom_fields.deleted");
        });
    }
}
