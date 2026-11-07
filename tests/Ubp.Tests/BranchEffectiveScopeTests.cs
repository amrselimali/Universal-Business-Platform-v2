using Ubp.Application.Services;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Xunit;

namespace Ubp.Tests;

/// <summary>BranchScope.Effective: إلغاء الفلتر لا يعرض كل الفروع لغير السوبر أدمن (0096).</summary>
public sealed class BranchEffectiveScopeTests
{
    private static ActorContext Limited(Guid allowed, Guid? current = null) => new()
    {
        UserId = Guid.NewGuid(), Username = "u", Language = "ar",
        AllowedBranchIds = new[] { allowed }, AllBranchIds = new[] { allowed },
        CurrentBranchId = current, TraceId = "t"
    };

    [Fact]
    public void Null_filter_falls_back_to_current_or_first_allowed_branch()
    {
        var allowed = Guid.NewGuid();
        var withCurrent = BranchScope.Effective(Limited(allowed, allowed), null);
        Assert.Equal(allowed, withCurrent);

        var noCurrent = BranchScope.Effective(Limited(allowed), null);
        Assert.Equal(allowed, noCurrent);
    }

    [Fact]
    public void Superadmin_null_filter_stays_null()
    {
        var super = new ActorContext
        { UserId = Guid.NewGuid(), Username = "sa", IsSuperAdmin = true, TraceId = "t" };
        Assert.Null(BranchScope.Effective(super, null));
    }

    [Fact]
    public void Foreign_branch_is_forbidden()
    {
        var allowed = Guid.NewGuid();
        Assert.Throws<ForbiddenException>(() =>
            BranchScope.Effective(Limited(allowed), Guid.NewGuid()));
    }
}