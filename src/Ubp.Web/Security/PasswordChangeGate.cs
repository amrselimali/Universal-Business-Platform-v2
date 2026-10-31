namespace Ubp.Web.Security;

/// <summary>
/// بوابة واحدة لإجبار تغيير كلمة المرور (الترحيل 0036)، مشتركة بين الـ middleware
/// ومكوّنات Blazor حتى لا يختلف القرار بين طلب مستند وتنقّل داخلي.
/// </summary>
public static class PasswordChangeGate
{
    public const string ChangePasswordPath = "/change-password";
    public const string ErrorKey = "auth.password_change_required";

    private static readonly string[] AllowedPaths =
    [
        ChangePasswordPath,
        "/auth/logout",
        "/api/v1/auth/logout",
        "/api/v1/system/me"
    ];

    private static readonly string[] AssetPrefixes =
    [
        "/css", "/js", "/images", "/_content", "/_framework", "/_blazor", "/favicon"
    ];

    public static bool IsBlocked(PathString path, bool mustChangePassword)
    {
        if (!mustChangePassword) return false;
        if (AllowedPaths.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
            return false;
        return !AssetPrefixes.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>يُستخدم في الواجهة: هل المسار الحالي هو شاشة تغيير كلمة المرور؟</summary>
    public static bool IsChangePasswordRoute(string baseRelativePath) =>
        baseRelativePath.Split('?', '#')[0]
            .Equals("change-password", StringComparison.OrdinalIgnoreCase);
}
