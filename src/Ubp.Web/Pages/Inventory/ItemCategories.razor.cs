using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Security;

namespace Ubp.Web.Pages.Inventory;

public partial class ItemCategories
{
    [Inject] private ItemCategoryService Categories { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;

    private MudTable<ItemCategory> _table = default!;
    private string? _search;
    private bool _includeInactive;

    protected override string ScreenCode => ScreenCodes.ItemCategories;
    protected override Task OnLoadAsync() => Task.CompletedTask;

    private string Name(ItemCategory category) =>
        T.Language == "en" && !string.IsNullOrWhiteSpace(category.NameEn) ? category.NameEn! : category.NameAr;

    private async Task<TableData<ItemCategory>> LoadServerData(TableState state, CancellationToken ct)
    {
        var result = await Categories.ListAsync(Actor!, new PageRequest
        {
            Page = state.Page + 1, PageSize = 25, Search = _search, IncludeInactive = _includeInactive,
            SortBy = state.SortLabel ?? "code", Descending = state.SortDirection == SortDirection.Descending
        }, ct);
        return new TableData<ItemCategory> { Items = result.Items, TotalItems = result.Total };
    }

    private async Task ReloadAsync() => await _table.ReloadServerData();
    private async Task OnFilterChanged(bool value) { _includeInactive = value; await ReloadAsync(); }

    private async Task AddAsync()
    {
        var dialog = await DialogService.ShowAsync<ItemCategoryDialog>(T.T("common.add"), new DialogParameters());
        if ((await dialog.Result) is { Canceled: false }) { await ReloadAsync(); ShowSuccess("common.saved"); }
    }

    private async Task EditAsync(ItemCategory category)
    {
        var dialog = await DialogService.ShowAsync<ItemCategoryDialog>(T.T("common.edit"),
            new DialogParameters { ["Model"] = category });
        if ((await dialog.Result) is { Canceled: false }) { await ReloadAsync(); ShowSuccess("common.saved"); }
    }

    private async Task ToggleAsync(ItemCategory category, bool value) => await RunAsync(async () =>
    {
        await Categories.SetActiveAsync(Actor!, category.Id, value);
        await ReloadAsync();
        ShowSuccess("common.saved");
    });

    private async Task DeleteAsync(ItemCategory category)
    {
        var confirmed = await DialogService.ShowMessageBoxAsync(T.T("common.confirm_delete"), category.NameAr,
            yesText: T.T("common.delete"), cancelText: T.T("common.cancel"));
        if (confirmed != true) return;
        await RunAsync(async () =>
        {
            await Categories.DeleteAsync(Actor!, category.Id);
            await ReloadAsync();
            ShowSuccess("common.deleted");
        });
    }
}
