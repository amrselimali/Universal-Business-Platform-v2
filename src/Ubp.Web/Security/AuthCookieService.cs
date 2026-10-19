using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;
using Ubp.Application.Contracts;

namespace Ubp.Web.Security;

public static class AuthCookieService
{
    public const string Scheme = "UbpCookie";
    public const string RefreshCookie = "ubp_refresh";
    public const string LanguageCookie = "ubp_lang";

    public static async Task SignInAsync(HttpContext context, LoginResult result)
    {
        var user = result.User!;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new("name", user.Name ?? user.Username),
            new("session_id", result.SessionId!.Value.ToString()),
            new("is_admin", user.IsAdmin ? "true" : "false"),
            new("is_super_admin", user.IsSuperAdmin ? "true" : "false"),
            new("language", user.Language)
        };
        if (result.BranchId.HasValue)
            claims.Add(new Claim("branch_id", result.BranchId.Value.ToString()));

        var identity = new ClaimsIdentity(claims, Scheme);
        await context.SignInAsync(Scheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = false });

        if (!string.IsNullOrWhiteSpace(result.RefreshToken))
        {
            context.Response.Cookies.Append(RefreshCookie, result.RefreshToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = context.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Path = "/",
                Expires = DateTimeOffset.UtcNow.AddDays(30)
            });
        }

        context.Response.Cookies.Append(LanguageCookie, user.Language, new CookieOptions
        {
            HttpOnly = false,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = DateTimeOffset.UtcNow.AddDays(365)
        });
    }

    public static async Task SignOutAsync(HttpContext context)
    {
        await context.SignOutAsync(Scheme);
        context.Response.Cookies.Delete(RefreshCookie, new CookieOptions { Path = "/" });
    }

    public static string SafeReturnUrl(string? returnUrl, string fallback = "/")
    {
        if (string.IsNullOrWhiteSpace(returnUrl)) return fallback;
        if (!returnUrl.StartsWith('/') || returnUrl.StartsWith("//")) return fallback;
        return returnUrl;
    }
}
