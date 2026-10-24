using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Web.Services;

namespace Ubp.Web.Pages.Accounting;

public partial class CategoryAccountDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;
    [Parameter] public IReadOnlyList<ItemCategory> Categories { get; set; } = Array.Empty<ItemCategory>();
    [Parameter] public IReadOnlyList<Account> Accounts { get; set; } = Array.Empty<Account>();

    [Inject] private AccountingSettingsService Settings { get; set; } = default!;
    [Inject] private UserContext UserContext { get; set; } = default!;
    [Inject] private UiCulture T { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private MudForm _form = default!;
    private readonly ItemCategoryAccountInput _input = new();
    private bool _busy;
    private bool _english;

    private sealed record AccountField(string LabelKey, bool Required,
        Func<ItemCategoryAccountInput, Guid?> Get, Action<ItemCategoryAccountInput, Guid?> Set);

    private IReadOnlyList<AccountField> AccountFields => new[]
    {
        new AccountField("accounting.inventory_account", true,
            i => i.InventoryAccountId == Guid.Empty ? null : i.InventoryAccountId,
            (i, v) => i.InventoryAccountId = v ?? Guid.Empty),
        new AccountField("accounting.cogs_account", true,
            i => i.CogsAccountId == Guid.Empty ? null : i.CogsAccountId,
            (i, v) => i.CogsAccountId = v ?? Guid.Empty),
        new AccountField("accounting.adjustment_gain", false,
            i => i.AdjustmentGainAccountId, (i, v) => i.AdjustmentGainAccountId = v),
        new AccountField("accounting.adjustment_loss", false,
            i => i.AdjustmentLossAccountId, (i, v) => i.AdjustmentLossAccountId = v)
    };

    protected override void OnInitialized() => _english = T.Language == "en";

    private async Task SaveAsync()
    {
        await _form.Validate();
        if (_input.CategoryId == Guid.Empty || _input.InventoryAccountId == Guid.Empty ||
            _input.CogsAccountId == Guid.Empty)
        {
            Snackbar.Add(T.T("validation.required_fields"), Severity.Warning);
            return;
        }

        _busy = true;
        try
        {
            var actor = await UserContext.RequireAsync();
            await Settings.SaveCategoryAccountAsync(actor, _input);
            MudDialog.Close(DialogResult.Ok(true));
        }
        catch (AppException ex) { Snackbar.Add(T.T(ex.Code), Severity.Error); }
        finally { _busy = false; }
    }
}
