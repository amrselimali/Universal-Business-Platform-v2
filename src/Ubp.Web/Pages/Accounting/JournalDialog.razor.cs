using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Web.Services;

namespace Ubp.Web.Pages.Accounting;

public partial class JournalDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;
    [Parameter] public Journal? Model { get; set; }

    [Inject] private JournalService Journals { get; set; } = default!;
    [Inject] private AccountService Accounts { get; set; } = default!;
    [Inject] private UserContext UserContext { get; set; } = default!;
    [Inject] private UiCulture T { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private MudForm _form = default!;
    private JournalInput _input = new();
    private IReadOnlyList<Account> _accounts = Array.Empty<Account>();
    private bool _busy;
    private bool _english;

    protected override async Task OnInitializedAsync()
    {
        var actor = await UserContext.GetAsync();
        _english = T.Language == "en";
        if (actor is not null) _accounts = await Accounts.PostableAsync(actor);

        if (Model is null) return;
        _input = new JournalInput
        {
            Code = Model.Code, NameAr = Model.NameAr, NameEn = Model.NameEn,
            JournalType = Model.JournalType, DefaultAccountId = Model.DefaultAccountId,
            AllowManual = Model.AllowManual, SortOrder = Model.SortOrder, IsActive = Model.IsActive
        };
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
            var saved = Model is null
                ? await Journals.CreateAsync(actor, _input)
                : await Journals.UpdateAsync(actor, Model.Id, Model.Version, _input);
            MudDialog.Close(DialogResult.Ok(saved));
        }
        catch (AppException ex) { Snackbar.Add(T.T(ex.Code), Severity.Error); }
        finally { _busy = false; }
    }
}
