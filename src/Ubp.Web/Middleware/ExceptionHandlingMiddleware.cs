using System.Text.Json;
using Ubp.Core.Results;

namespace Ubp.Web.Middleware;

public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next; _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (AppException ex)
        {
            _logger.LogWarning(ex, "Application error {Code} path {Path}", ex.Code, context.Request.Path);
            await HandleAsync(context, ex.StatusCode, ex.Code, ex.Args);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error path {Path}", context.Request.Path);
            await HandleAsync(context, 500, "error.generic", new Dictionary<string, string>());
        }
    }

    private static async Task HandleAsync(HttpContext context, int status, string code,
        IReadOnlyDictionary<string, string> args)
    {
        if (context.Response.HasStarted) return;
        var traceId = context.Items["TraceId"]?.ToString() ?? context.TraceIdentifier;

        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.Clear();
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                error = code, args, traceId
            }));
            return;
        }

        if (status == 403)
        {
            context.Response.Redirect("/forbidden");
            return;
        }
        if (status == 404)
        {
            context.Response.Redirect("/not-found");
            return;
        }
        context.Response.Redirect($"/error?traceId={Uri.EscapeDataString(traceId)}");
    }
}
