using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Xunit;

namespace Ubp.Tests;

public sealed class BranchScopeTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public BranchScopeTests(TestDb db) => _db = db;

    private async Task<(ActorContext Limited, Guid BranchA, Guid BranchB)> SetupAsync(
        string suffix)
    {
        var admin = await _db.AdminActorAsync();
        var branchA = await _db.CreateBranchAsync("SC-A-" + suffix, "فرع أول " + suffix);
        var branchB = await _db.CreateBranchAsync("SC-B-" + suffix, "فرع ثانٍ " + suffix);
        var userId = await _db.CreateUserAsync("scope-" + suffix, "Passw0rd!", new[] { branchA });

        var limited = new ActorContext
        {
            UserId = userId, Username = "scope-" + suffix, Name = "مستخدم فرع",
            IsAdmin = true, IsSuperAdmin = false, Language = "ar",
            AllowedBranchIds = new[] { branchA }, TraceId = "test"
        };
        _ = admin;
        return (limited, branchA, branchB);
    }

    [Fact]
    public async Task Branch_options_are_limited_to_assigned_branches_for_non_super_admin()
    {
        var (limited, branchA, branchB) = await SetupAsync("1");

        var options = await _db.BranchService().OptionsAsync(limited);

        Assert.Contains(options, b => b.Id == branchA);
        Assert.DoesNotContain(options, b => b.Id == branchB);
    }

    [Fact]
    public async Task Super_admin_still_sees_every_branch()
    {
        var admin = await _db.AdminActorAsync();
        var (_, branchA, branchB) = await SetupAsync("2");

        var options = await _db.BranchService().OptionsAsync(admin);

        Assert.Contains(options, b => b.Id == branchA);
        Assert.Contains(options, b => b.Id == branchB);
    }

    [Fact]
    public async Task Writing_a_document_in_a_foreign_branch_is_forbidden()
    {
        var (limited, _, branchB) = await SetupAsync("3");

        var error = await Assert.ThrowsAsync<ForbiddenException>(() =>
            _db.BookingService().CreateAsync(limited, new BookingInput
            {
                BranchId = branchB, CustomerId = Guid.NewGuid(), BookingDate = DateTime.Today
            }));
        Assert.NotNull(error);
    }
}
