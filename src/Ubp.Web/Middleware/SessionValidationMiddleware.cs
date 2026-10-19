using Microsoft.AspNetCore.Authentication;
using Ubp.Application.Contracts;
using Ubp.Application.Services;
using Ubp.Web.Security;

namespace Ubp.Web.Middleware;

public sealed class SessionValidationMiddleware
{
    private readonly RequestDelegate _next;

    public SessionValidationMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        if (ShouldSkip(path) || context.User.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        var sidValue = context.User.FindFirst("session_id")?.Value;
        if (!Guid.TryParse(sidValue, out var sessionId))
        {
            await _next(context);
            return;
        }

        var auth = context.RequestServices.GetRequiredService<AuthService>();
        var traceId = context.Items["TraceId"]?.ToString() ?? context.TraceIdentifier;
        var ip = context.Connection.RemoteIpAddress?.ToString();
        var session = await auth.ValidateSessionAsync(sessionId, ip, traceId);
        if (session is null)
        {
            await AuthCookieService.SignOutAsync(context);
            if (path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsync("{\"error\":\"auth.session_expired\"}");
            }
            else
            {
                context.Response.Redirect("/login?expired=1");
            }
            return;
        }

        context.Items["session"] = session;
        await _next(context);
    }

    private static bool ShouldSkip(PathString path) =>
        path.StartsWithSegments("/_blazor") ||
        path.StartsWithSegments("/_framework") ||
        path.StartsWithSegments("/_content") ||
        path.StartsWithSegments("/css") ||
        path.StartsWithSegments("/js") ||
        path.StartsWithSegments("/images") ||
        path.StartsWithSegments("/login") ||
        path.StartsWithSegments("/auth/login") ||
        path.StartsWithSegments("/error") ||
        path.StartsWithSegments("/forbidden") ||
        path.StartsWithSegments("/not-found") ||
        path.StartsWithSegments("/api/v1/auth/login") ||
        path.StartsWithSegments("/api/v1/auth/refresh");
}
