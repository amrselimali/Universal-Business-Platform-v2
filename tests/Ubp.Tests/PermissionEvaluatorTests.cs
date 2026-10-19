using Xunit;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Security;

namespace Ubp.Tests;

public sealed class PermissionEvaluatorTests
{
    [Fact]
    public void No_grants_and_no_override_means_denied()
    {
        var result = PermissionEvaluator.Evaluate(Array.Empty<PermissionEntry>(),
            Array.Empty<PermissionEntry>(), false, ScreenCodes.Branches, ActionCodes.View);
        Assert.False(result.Allowed);
        Assert.Equal("none", result.Source);
    }

    [Fact]
    public void Role_grant_allows_and_carries_scope()
    {
        var grants = new[]
        {
            new PermissionEntry { ScreenCode = ScreenCodes.Branches, ActionCode = ActionCodes.View,
                IsAllowed = true, Scope = PermissionScope.Branches }
        };
        var result = PermissionEvaluator.Evaluate(grants, Array.Empty<PermissionEntry>(), false,
            ScreenCodes.Branches, ActionCodes.View);
        Assert.True(result.Allowed);
        Assert.Equal(PermissionScope.Branches, result.Scope);
        Assert.Equal("role", result.Source);
    }

    [Fact]
    public void Individual_deny_overrides_role_grant()
    {
        var grants = new[]
        {
            new PermissionEntry { ScreenCode = ScreenCodes.Branches, ActionCode = ActionCodes.View,
                IsAllowed = true, Scope = PermissionScope.All }
        };
        var overrides = new[]
        {
            new PermissionEntry { ScreenCode = ScreenCodes.Branches, ActionCode = ActionCodes.View,
                IsAllowed = false, Scope = 0 }
        };
        var result = PermissionEvaluator.Evaluate(grants, overrides, false,
            ScreenCodes.Branches, ActionCodes.View);
        Assert.False(result.Allowed);
        Assert.True(result.HasOverride);
    }

    [Fact]
    public void Individual_allow_overrides_missing_role_grant()
    {
        var overrides = new[]
        {
            new PermissionEntry { ScreenCode = ScreenCodes.AuditLog, ActionCode = ActionCodes.View,
                IsAllowed = true, Scope = PermissionScope.Own }
        };
        var result = PermissionEvaluator.Evaluate(Array.Empty<PermissionEntry>(), overrides, false,
            ScreenCodes.AuditLog, ActionCodes.View);
        Assert.True(result.Allowed);
        Assert.Equal("user", result.Source);
    }

    [Fact]
    public void Admin_bypasses_every_permission_with_all_scope()
    {
        var result = PermissionEvaluator.Evaluate(Array.Empty<PermissionEntry>(),
            Array.Empty<PermissionEntry>(), true, ScreenCodes.DatabaseAdmin, ActionCodes.RunMigrations);
        Assert.True(result.Allowed);
        Assert.Equal(PermissionScope.All, result.Scope);
        Assert.Equal("admin", result.Source);
    }

    [Fact]
    public void Multiple_role_grants_take_the_broadest_scope()
    {
        var grants = new[]
        {
            new PermissionEntry { ScreenCode = ScreenCodes.Users, ActionCode = ActionCodes.View,
                IsAllowed = true, Scope = PermissionScope.Own },
            new PermissionEntry { ScreenCode = ScreenCodes.Users, ActionCode = ActionCodes.View,
                IsAllowed = true, Scope = PermissionScope.All }
        };
        var result = PermissionEvaluator.Evaluate(grants, Array.Empty<PermissionEntry>(), false,
            ScreenCodes.Users, ActionCodes.View);
        Assert.True(result.Allowed);
        Assert.Equal(PermissionScope.All, result.Scope);
    }
}
