using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Ubp.Web.Components.Shared;

/// <summary>ErrorBoundary يسجل الاستثناء برقم تتبع ويعرض رسالة عامة للمستخدم.</summary>
public sealed class ErrorBoundaryWithLogging : ErrorBoundary
{
    [Inject] private ILogger<ErrorBoundaryWithLogging> Logger { get; set; } = default!;

    protected override Task OnErrorAsync(Exception exception)
    {
        Logger.LogError(exception, "Unhandled Blazor rendering error, trace {TraceId}",
            System.Diagnostics.Activity.Current?.TraceId.ToString());
        return base.OnErrorAsync(exception);
    }
}
