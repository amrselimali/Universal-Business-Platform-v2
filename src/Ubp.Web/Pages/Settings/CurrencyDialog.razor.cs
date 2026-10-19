using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Web.Services;

namespace Ubp.Web.Pages.Settings;

public partial class CurrencyDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;
    [Parameter] public Currency? Model { get; set; }
    [Inject] private CurrencyService Currencies { get; set; } = default!;
    [Inject] private UserContext UserContext { get; set; } = default!;
    [Inject] private UiCulture T { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private MudForm _form = default!;
    private CurrencyInput _input = new();
    private int _decimals = 2;
    private bool _busy;

    private bool IsEdit => Model is not null;

    protected override void OnInitialized()
    {
        if (Model is not null)
        {
            _input = new CurrencyInput
            {
                CurrencyCode = Model.CurrencyCode, NameAr = Model.NameAr, NameEn = Model.NameEn,
                Symbol = Model.Symbol, Decimals = Model.Decimals, IsBase = Model.IsBase
            };
            _decimals = Model.Decimals;
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
            _input.Decimals = (short)_decimals;
            Currency saved = Model is null
                ? await Currencies.CreateAsync(actor, _input)
                : await Currencies.UpdateAsync(actor, Model.Id, Model.Version, _input);
            MudDialog.Close(DialogResult.Ok(saved));
        }
        catch (AppException ex)
        {
            Snackbar.Add(T.T(ex.Code), Severity.Error);
        }
        finally { _busy = false; }
    }
}
