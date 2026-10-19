using Ubp.Core.Models;
using Ubp.Core.Security;

namespace Ubp.Application.Services;

/// <summary>منطق نقي قابل للاختبار: الدمج بين صلاحيات الأدوار والاستثناءات الفردية.</summary>
public static class PermissionEvaluator
{
    public static EffectivePermission Evaluate(
        IReadOnlyList<PermissionEntry> roleGrants,
        IReadOnlyList<PermissionEntry> overrides,
        bool isAdmin,
        string screenCode,
        string actionCode)
    {
        var result = new EffectivePermission
        {
            ScreenCode = screenCode,
            ActionCode = actionCode,
            Allowed = false,
            Scope = PermissionScope.Own,
            Source = "none"
        };

        if (isAdmin)
        {
            result.Allowed = true;
            result.Scope = PermissionScope.All;
            result.Source = "admin";
            return result;
        }

        var grants = roleGrants
            .Where(p => p.ScreenCode.Equals(screenCode, StringComparison.OrdinalIgnoreCase)
                     && p.ActionCode.Equals(actionCode, StringComparison.OrdinalIgnoreCase)
                     && p.IsAllowed)
            .ToList();

        if (grants.Count > 0)
        {
            result.RoleAllowed = true;
            result.RoleScope = grants.Max(p => PermissionScope.Normalize(p.Scope));
            result.Allowed = true;
            result.Scope = result.RoleScope;
            result.Source = "role";
        }

        var userOverride = overrides.FirstOrDefault(p =>
            p.ScreenCode.Equals(screenCode, StringComparison.OrdinalIgnoreCase) &&
            p.ActionCode.Equals(actionCode, StringComparison.OrdinalIgnoreCase));

        if (userOverride is not null)
        {
            result.HasOverride = true;
            result.Allowed = userOverride.IsAllowed;
            result.Scope = userOverride.IsAllowed ? PermissionScope.Normalize(userOverride.Scope) : PermissionScope.Own;
            result.Source = userOverride.IsAllowed ? "user" : "denied";
        }

        return result;
    }
}
