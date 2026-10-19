using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Results;
using Ubp.Web.Services;

namespace Ubp.Web.Pages.Users;

public partial class ResetPasswordDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;
    [Parameter] public Guid UserId { get; set; }
    [Parameter] public string Username { get; set; } = "";
    [Inject] private UserService Users { get; set; } = default!;
    [Inject] private UserContext UserContext { get; set; } = default!;
    [Inject] private UiCulture T { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private string _password = "";
    private string _confirm = "";
    private bool _busy;

    private void Cancel() => MudDialog.Cancel();

    private async Task SaveAsync()
    {
        if (_password != _confirm)
        {
            Snackbar.Add(T.T("validation.password_mismatch"), Severity.Error);
            return;
        }
        _busy = true;
        try
        {
            var actor = await UserContext.RequireAsync();
            await Users.ResetPasswordAsync(actor, UserId, _password);
            MudDialog.Close(DialogResult.Ok(true));
        }
        catch (AppException ex)
        {
            Snackbar.Add(T.T(ex.Code), Severity.Error);
        }
        finally { _busy = false; }
    }
}
