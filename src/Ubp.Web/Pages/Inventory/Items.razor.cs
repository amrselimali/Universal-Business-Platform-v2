using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Security;

namespace Ubp.Web.Pages.Inventory;

public partial class Items
{
    [Inject] private ItemService ItemService { get; set; } = default!;
    [Inject] private ItemCategoryService CategoryService { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;

    private MudTable<Item> _table = default!;
    private string? _search;
    private Guid? _categoryId;
    private bool _includeInactive;
    private IReadOnlyList<ItemCategory> _categories = Array.Empty<ItemCategory>();

    protected override string ScreenCode => ScreenCodes.Items;

    protected override async Task OnLoadAsync()
    {
        _categories = await CategoryService.OptionsAsync(Actor!);
    }

    private string Name(Item item) => T.Language == "en" && !string.IsNullOrWhiteSpace(item.NameEn) ? item.NameEn! : item.NameAr;
    private string CategoryName(ItemCategory category) =>
        T.Language == "en" && !string.IsNullOrWhiteSpace(category.NameEn) ? category.NameEn! : category.NameAr;

    private async Task<TableData<Item>> LoadServerData(TableState state, CancellationToken ct)
    {
        var result = await ItemService.ListAsync(Actor!, new PageRequest
        {
            Page = state.Page + 1, PageSize = 25, Search = _search, IncludeInactive = _includeInactive,
            SortBy = state.SortLabel ?? "code", Descending = state.SortDirection == SortDirection.Descending
        }, _categoryId, null, ct);
        return new TableData<Item> { Items = result.Items, TotalItems = result.Total };
    }

    private async Task ReloadAsync() => await _table.ReloadServerData();
    private async Task OnFilterChanged(bool value) { _includeInactive = value; await ReloadAsync(); }

    private async Task AddAsync()
    {
        var dialog = await DialogService.ShowAsync<ItemDialog>(T.T("common.add"), new DialogParameters());
        if ((await dialog.Result) is { Canceled: false }) { await ReloadAsync(); ShowSuccess("common.saved"); }
    }

    private async Task EditAsync(Item item)
    {
        var dialog = await DialogService.ShowAsync<ItemDialog>(T.T("common.edit"),
            new DialogParameters { ["Model"] = item });
        if ((await dialog.Result) is { Canceled: false }) { await ReloadAsync(); ShowSuccess("common.saved"); }
    }

    private async Task ToggleAsync(Item item, bool value) => await RunAsync(async () =>
    {
        await ItemService.SetActiveAsync(Actor!, item.Id, value);
        await ReloadAsync();
        ShowSuccess("common.saved");
    });

    private async Task DeleteAsync(Item item)
    {
        var confirmed = await DialogService.ShowMessageBoxAsync(T.T("common.confirm_delete"), item.NameAr,
            yesText: T.T("common.delete"), cancelText: T.T("common.cancel"));
        if (confirmed != true) return;
        await RunAsync(async () =>
        {
            await ItemService.DeleteAsync(Actor!, item.Id);
            await ReloadAsync();
            ShowSuccess("common.deleted");
        });
    }
}
