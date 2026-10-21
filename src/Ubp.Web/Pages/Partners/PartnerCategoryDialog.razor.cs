using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Web.Services;

namespace Ubp.Web.Pages.Partners;

public partial class PartnerCategoryDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;
    [Parameter] public PartnerKind Kind { get; set; } = PartnerKind.Customer;
    [Inject] private PartnerService Partners { get; set; } = default!;
    [Inject] private UserContext UserContext { get; set; } = default!;
    [Inject] private UiCulture T { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private IReadOnlyList<PartnerCategory> _categories = Array.Empty<PartnerCategory>();
    private string? _code;
    private string? _nameAr;
    private bool _busy;

    protected override async Task OnInitializedAsync() => await ReloadAsync();

    private async Task ReloadAsync()
    {
        var actor = await UserContext.RequireAsync();
        _categories = await Partners.ListCategoriesAsync(actor, Kind, true);
    }

    private string Name(PartnerCategory category) =>
        T.Language == "en" && !string.IsNullOrWhiteSpace(category.NameEn) ? category.NameEn! : category.NameAr;

    private async Task AddAsync()
    {
        _busy = true;
        try
        {
            var actor = await UserContext.RequireAsync();
            await Partners.CreateCategoryAsync(actor, Kind, new PartnerCategoryInput
            {
                Code = _code ?? "", NameAr = _nameAr ?? ""
            });
            _code = null; _nameAr = null;
            await ReloadAsync();
        }
        catch (AppException ex) { Snackbar.Add(T.T(ex.Code), Severity.Error); }
        finally { _busy = false; }
    }

    private async Task DeleteAsync(PartnerCategory category)
    {
        try
        {
            var actor = await UserContext.RequireAsync();
            await Partners.DeleteCategoryAsync(actor, Kind, category.Id);
            await ReloadAsync();
        }
        catch (AppException ex) { Snackbar.Add(T.T(ex.Code), Severity.Error); }
    }

    private void Close() => MudDialog.Close(DialogResult.Ok(true));
}
