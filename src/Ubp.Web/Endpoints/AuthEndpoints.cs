using Microsoft.AspNetCore.Antiforgery;
using Ubp.Application.Contracts;
using Ubp.Application.Services;
using Ubp.Web.Security;

namespace Ubp.Web.Endpoints;

public static partial class AuthEndpoints
{
    public sealed record ApiLoginRequest(string Username, string Password);

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").DisableAntiforgery();
        group.MapPost("/login", LoginForm);
        group.MapPost("/logout", LogoutForm);
        group.MapPost("/branch", SwitchBranch);
        group.MapPost("/language", SwitchLanguage);

        var api = app.MapGroup("/api/v1/auth").DisableAntiforgery();
        api.MapPost("/login", ApiLogin);
        api.MapPost("/refresh", ApiRefresh);
        api.MapPost("/logout", ApiLogout).RequireAuthorization();
        return app;
    }

    private static async Task<IResult> LoginForm(HttpContext context, AuthService auth,
        IAntiforgery antiforgery)
    {
        if (!await ValidateAntiforgeryAsync(context, antiforgery))
            return Results.Redirect("/login?error=error.antiforgery");

        var form = await context.Request.ReadFormAsync();
        var returnUrl = AuthCookieService.SafeReturnUrl(form["returnUrl"].ToString());
        var traceId = context.Items["TraceId"]?.ToString() ?? context.TraceIdentifier;
        var ip = context.Connection.RemoteIpAddress?.ToString();
        var result = await auth.LoginAsync(form["username"].ToString(), form["password"].ToString(),
            ip, traceId);
        if (!result.Success)
            return Results.Redirect($"/login?error={Uri.EscapeDataString(result.ErrorKey!)}");

        await AuthCookieService.SignInAsync(context, result);
        return Results.Redirect(returnUrl);
    }

    private static async Task<IResult> ApiLogin(HttpContext context, ApiLoginRequest request,
        AuthService auth)
    {
        var traceId = context.Items["TraceId"]?.ToString() ?? context.TraceIdentifier;
        var ip = context.Connection.RemoteIpAddress?.ToString();
        var result = await auth.LoginAsync(request.Username, request.Password, ip, traceId);
        if (!result.Success)
            return Results.Json(new { error = result.ErrorKey }, statusCode: 401);

        await AuthCookieService.SignInAsync(context, result);
        return Results.Ok(new
        {
            accessToken = result.AccessToken,
            refreshToken = result.RefreshToken,
            sessionId = result.SessionId,
            branchId = result.BranchId
        });
    }

    private static async Task<IResult> ApiRefresh(HttpContext context, AuthService auth)
    {
        if (!context.Request.Cookies.TryGetValue(AuthCookieService.RefreshCookie, out var raw))
            return Results.Json(new { error = "auth.invalid_refresh" }, statusCode: 401);
        var traceId = context.Items["TraceId"]?.ToString() ?? context.TraceIdentifier;
        var ip = context.Connection.RemoteIpAddress?.ToString();
        var result = await auth.RefreshAsync(raw, ip, traceId);
        if (!result.Success)
            return Results.Json(new { error = result.ErrorKey }, statusCode: 401);
        await AuthCookieService.SignInAsync(context, result);
        return Results.Ok(new { accessToken = result.AccessToken, refreshToken = result.RefreshToken });
    }

    private static async Task<IResult> ApiLogout(HttpContext context, AuthService auth)
    {
        var session = context.Items["session"] as SessionUser;
        if (session is not null)
        {
            var traceId = context.Items["TraceId"]?.ToString() ?? context.TraceIdentifier;
            var ip = context.Connection.RemoteIpAddress?.ToString();
            await auth.LogoutAsync(session.SessionId, session.User.Id, session.BranchId, ip, traceId);
        }
        await AuthCookieService.SignOutAsync(context);
        return Results.Ok(new { success = true });
    }
}
