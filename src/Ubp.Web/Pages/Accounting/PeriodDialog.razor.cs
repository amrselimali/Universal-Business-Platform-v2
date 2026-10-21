using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Web.Services;

namespace Ubp.Web.Pages.Accounting;

public partial class PeriodDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;
    [Parameter] public FiscalPeriod? Model { get; set; }
    [Inject] private PeriodService Periods { get; set; } = default!;
    [Inject] private UserContext UserContext { get; set; } = default!;
    [Inject] private UiCulture T { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private MudForm _form = default!;
    private FiscalPeriodInput _input = new();
    private DateTime? _start = DateTime.Today;
    private DateTime? _end = DateTime.Today;
    private bool _busy;
    private bool IsEdit => Model is not null;

    protected override void OnInitialized()
    {
        if (Model is null) return;
        _input = new FiscalPeriodInput
        {
            Code = Model.Code, NameAr = Model.NameAr, NameEn = Model.NameEn,
            StartDate = Model.StartDate, EndDate = Model.EndDate
        };
        _start = Model.StartDate;
        _end = Model.EndDate;
    }

    private void Cancel() => MudDialog.Cancel();

    private async Task SaveAsync()
    {
        await _form.Validate();
        if (!_form.IsValid || _start is null || _end is null) return;
        _busy = true;
        try
        {
            var actor = await UserContext.RequireAsync();
            _input.StartDate = _start.Value.Date;
            _input.EndDate = _end.Value.Date;
            var saved = Model is null
                ? await Periods.CreateAsync(actor, _input)
                : await Periods.UpdateAsync(actor, Model.Id, Model.Version, _input);
            MudDialog.Close(DialogResult.Ok(saved));
        }
        catch (AppException ex) { Snackbar.Add(T.T(ex.Code), Severity.Error); }
        finally { _busy = false; }
    }
}
