using Microsoft.AspNetCore.Components;
using MudBlazor;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Web.Services;

namespace Ubp.Web.Pages.Qa;

public partial class QaResultsDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;
    [Parameter, EditorRequired] public QaRunSummary Run { get; set; } = new();
    [Inject] private QaService Qa { get; set; } = default!;
    [Inject] private UserContext UserContext { get; set; } = default!;
    [Inject] private UiCulture T { get; set; } = default!;

    private IReadOnlyList<QaTestResult> _results = Array.Empty<QaTestResult>();

    protected override async Task OnInitializedAsync()
    {
        var actor = await UserContext.RequireAsync();
        _results = await Qa.GetResultsAsync(actor, Run.Id);
    }

    private void Close() => MudDialog.Close();
}
