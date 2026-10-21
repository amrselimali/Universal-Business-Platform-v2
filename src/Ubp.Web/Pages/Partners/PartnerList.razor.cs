using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Security;

namespace Ubp.Web.Pages.Partners;

public partial class PartnerList
{
    [Parameter] public PartnerKind Kind { get; set; } = PartnerKind.Customer;
    [Inject] private PartnerService Partners { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;

    private MudTable<Partner> _table = default!;
    private string? _search;
    private Guid? _categoryId;
    private bool _includeInactive;
    private IReadOnlyList<PartnerCategory> _categories = Array.Empty<PartnerCategory>();

    protected override string ScreenCode => Kind == PartnerKind.Customer ? ScreenCodes.Customers : ScreenCodes.Suppliers;
    private string ScreenTitleKey => Kind == PartnerKind.Customer ? "screen.customers" : "screen.suppliers";

    protected override async Task OnLoadAsync()
    {
        _categories = await Partners.ListCategoriesAsync(Actor!, Kind, false);
    }

    private string Name(Partner partner) =>
        T.Language == "en" && !string.IsNullOrWhiteSpace(partner.NameEn) ? partner.NameEn! : partner.NameAr;
    private string CategoryName(PartnerCategory category) =>
        T.Language == "en" && !string.IsNullOrWhiteSpace(category.NameEn) ? category.NameEn! : category.NameAr;

    private async Task<TableData<Partner>> LoadServerData(TableState state, CancellationToken ct)
    {
        var result = await Partners.ListAsync(Actor!, Kind, new PageRequest
        {
            Page = state.Page + 1, PageSize = 25, Search = _search, IncludeInactive = _includeInactive,
            SortBy = state.SortLabel ?? "code", Descending = state.SortDirection == SortDirection.Descending
        }, _categoryId, ct);
        return new TableData<Partner> { Items = result.Items, TotalItems = result.Total };
    }

    private async Task ReloadAsync() => await _table.ReloadServerData();
    private async Task OnFilterChanged(bool value) { _includeInactive = value; await ReloadAsync(); }

    private async Task AddAsync()
    {
        var dialog = await DialogService.ShowAsync<PartnerDialog>(T.T("common.add"),
            new DialogParameters { ["Kind"] = Kind });
        if ((await dialog.Result) is { Canceled: false }) { await ReloadAsync(); ShowSuccess("common.saved"); }
    }

    private async Task EditAsync(Partner partner)
    {
        var dialog = await DialogService.ShowAsync<PartnerDialog>(T.T("common.edit"),
            new DialogParameters { ["Kind"] = Kind, ["Model"] = partner });
        if ((await dialog.Result) is { Canceled: false }) { await ReloadAsync(); ShowSuccess("common.saved"); }
    }

    private async Task ManageCategoriesAsync()
    {
        var dialog = await DialogService.ShowAsync<PartnerCategoryDialog>(T.T("partners.categories"),
            new DialogParameters { ["Kind"] = Kind });
        await dialog.Result;
        _categories = await Partners.ListCategoriesAsync(Actor!, Kind, false);
        await ReloadAsync();
    }

    private async Task ToggleAsync(Partner partner, bool value) => await RunAsync(async () =>
    {
        await Partners.SetActiveAsync(Actor!, Kind, partner.Id, value);
        await ReloadAsync();
        ShowSuccess("common.saved");
    });

    private async Task DeleteAsync(Partner partner)
    {
        var confirmed = await DialogService.ShowMessageBoxAsync(T.T("common.confirm_delete"), partner.NameAr,
            yesText: T.T("common.delete"), cancelText: T.T("common.cancel"));
        if (confirmed != true) return;
        await RunAsync(async () =>
        {
            await Partners.DeleteAsync(Actor!, Kind, partner.Id);
            await ReloadAsync();
            ShowSuccess("common.deleted");
        });
    }
}
