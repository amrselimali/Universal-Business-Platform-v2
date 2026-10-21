using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Web.Services;

namespace Ubp.Web.Pages.Accounting;

public partial class PeriodDetailDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;
    [Parameter] public Guid Id { get; set; }
    [Inject] private PeriodService Periods { get; set; } = default!;
    [Inject] private BranchService Branches { get; set; } = default!;
    [Inject] private PermissionService Permissions { get; set; } = default!;
    [Inject] private UserContext UserContext { get; set; } = default!;
    [Inject] private UiCulture T { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private PeriodDetail? _detail;
    private IReadOnlyList<Branch> _branches = Array.Empty<Branch>();
    private string _module = PeriodModules.Inventory;
    private string _scope = PeriodScopes.Branch;
    private Guid? _branchId;
    private string? _reason;
    private bool _busy;
    private bool _canClose;
    private bool _canReopen;

    private bool CanClose => _canClose;
    private bool CanReopen => _canReopen;

    protected override async Task OnInitializedAsync()
    {
        var actor = await UserContext.RequireAsync();
        _detail = await Periods.GetDetailAsync(actor, Id);
        _branches = await Branches.OptionsAsync(actor);
        _branchId = actor.CurrentBranchId ?? _branches.FirstOrDefault()?.Id;
        _canClose = await Permissions.HasAsync(actor, ScreenCodes.FinancialPeriods, ActionCodes.ClosePeriod);
        _canReopen = await Permissions.HasAsync(actor, ScreenCodes.FinancialPeriods, ActionCodes.ReopenPeriod);
    }

    private async Task ReloadAsync()
    {
        var actor = await UserContext.RequireAsync();
        _detail = await Periods.GetDetailAsync(actor, Id);
    }

    private async Task CloseAsync()
    {
        _busy = true;
        try
        {
            var actor = await UserContext.RequireAsync();
            await Periods.CloseAsync(actor, Id, _scope, _scope == PeriodScopes.Branch ? _branchId : null, _module);
            await ReloadAsync();
            Snackbar.Add(T.T("periods.closed_ok"), Severity.Success);
        }
        catch (AppException ex) { Snackbar.Add(ErrorText(ex), Severity.Error); }
        finally { _busy = false; }
    }

    private async Task ReopenAsync()
    {
        _busy = true;
        try
        {
            var actor = await UserContext.RequireAsync();
            await Periods.ReopenAsync(actor, Id, _scope, _scope == PeriodScopes.Branch ? _branchId : null,
                _module, _reason ?? "");
            await ReloadAsync();
            Snackbar.Add(T.T("periods.reopened_ok"), Severity.Success);
        }
        catch (AppException ex) { Snackbar.Add(ErrorText(ex), Severity.Error); }
        finally { _busy = false; }
    }

    private async Task FinalizeAsync()
    {
        _busy = true;
        try
        {
            var actor = await UserContext.RequireAsync();
            await Periods.FinalizeAsync(actor, Id);
            await ReloadAsync();
            Snackbar.Add(T.T("periods.finalized_ok"), Severity.Success);
        }
        catch (AppException ex) { Snackbar.Add(ErrorText(ex), Severity.Error); }
        finally { _busy = false; }
    }

    private string ErrorText(AppException ex)
    {
        var text = T.T(ex.Code);
        foreach (var arg in ex.Args)
            text = text.Replace("{" + arg.Key + "}", arg.Value);
        return text;
    }

    private void Close() => MudDialog.Close(DialogResult.Ok(true));
}
