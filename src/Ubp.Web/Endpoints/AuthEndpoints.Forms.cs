using Microsoft.AspNetCore.Antiforgery;
using Ubp.Application.Contracts;
using Ubp.Application.Services;
using Ubp.Web.Security;

namespace Ubp.Web.Endpoints;

public static partial class AuthEndpoints
{
    private static async Task<IResult> LogoutForm(HttpContext context, AuthService auth,
        IAntiforgery antiforgery)
    {
        if (!await ValidateAntiforgeryAsync(context, antiforgery))
            return Results.Redirect("/login?error=error.antiforgery");

        var session = context.Items["session"] as SessionUser;
        if (session is not null)
        {
            var traceId = context.Items["TraceId"]?.ToString() ?? context.TraceIdentifier;
            var ip = context.Connection.RemoteIpAddress?.ToString();
            await auth.LogoutAsync(session.SessionId, session.User.Id, session.BranchId, ip, traceId);
        }
        await AuthCookieService.SignOutAsync(context);
        return Results.Redirect("/login");
    }

    private static async Task<IResult> SwitchBranch(HttpContext context, AuthService auth,
        IAntiforgery antiforgery)
    {
        if (!await ValidateAntiforgeryAsync(context, antiforgery))
            return Results.Redirect("/?error=error.antiforgery");
        var session = context.Items["session"] as SessionUser;
        if (session is null) return Results.Redirect("/login?expired=1");

        var form = await context.Request.ReadFormAsync();
        var raw = form["branchId"].ToString();
        Guid? branchId = Guid.TryParse(raw, out var parsed) ? parsed : null;
        var ok = await auth.SwitchBranchAsync(session.SessionId, session.User.Id, branchId);
        if (!ok) return Results.Redirect("/forbidden");

        var returnUrl = AuthCookieService.SafeReturnUrl(form["returnUrl"].ToString());
        return Results.Redirect(returnUrl);
    }

    private static async Task<IResult> SwitchLanguage(HttpContext context, AuthService auth,
        IAntiforgery antiforgery)
    {
        if (!await ValidateAntiforgeryAsync(context, antiforgery))
            return Results.Redirect("/?error=error.antiforgery");
        var session = context.Items["session"] as SessionUser;
        var form = await context.Request.ReadFormAsync();
        var language = form["language"].ToString() == "en" ? "en" : "ar";
        if (session is not null)
            await auth.UpdateLanguageAsync(session.User.Id, language);

        context.Response.Cookies.Append(AuthCookieService.LanguageCookie, language, new CookieOptions
        {
            HttpOnly = false, SameSite = SameSiteMode.Lax, Path = "/",
            Secure = context.Request.IsHttps, Expires = DateTimeOffset.UtcNow.AddDays(365)
        });
        return Results.Redirect(AuthCookieService.SafeReturnUrl(form["returnUrl"].ToString()));
    }

    private static async Task<bool> ValidateAntiforgeryAsync(HttpContext context,
        IAntiforgery antiforgery)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }
}
