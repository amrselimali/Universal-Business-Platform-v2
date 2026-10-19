using System.Diagnostics;
using Serilog.Context;

namespace Ubp.Web.Middleware;

public sealed class TraceMiddleware
{
    public const string TraceIdHeader = "X-Trace-Id";
    private readonly RequestDelegate _next;

    public TraceMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var traceId = Activity.Current?.TraceId.ToString();
        if (string.IsNullOrWhiteSpace(traceId))
            traceId = Guid.NewGuid().ToString("N");
        context.TraceIdentifier = traceId;
        context.Items["TraceId"] = traceId;
        context.Response.Headers[TraceIdHeader] = traceId;
        using (LogContext.PushProperty("TraceId", traceId))
        {
            await _next(context);
        }
    }
}
