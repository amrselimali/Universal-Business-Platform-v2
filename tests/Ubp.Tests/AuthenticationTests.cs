using Ubp.Core.Models;
using Ubp.Infrastructure.Security;
using Xunit;

namespace Ubp.Tests;

public sealed class AuthenticationTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public AuthenticationTests(TestDb db) => _db = db;

    [Fact]
    public async Task Seeded_super_admin_can_login_with_documented_password()
    {
        var auth = _db.Auth();
        var result = await auth.LoginAsync("admin", "Admin@123", "127.0.0.1", "trace-login");
        Assert.True(result.Success);
        Assert.NotNull(result.SessionId);
        Assert.True(result.User!.IsSuperAdmin);
    }

    [Fact]
    public async Task Wrong_password_locks_the_account_and_correct_password_is_rejected_until_unlock()
    {
        const string password = "Str0ng!Pass";
        var id = await CreateIsolatedUserAsync("lock.user2", password);

        var auth = _db.Auth();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var failed = await auth.LoginAsync("lock.user2", "wrong-password", "127.0.0.1", "trace-fail");
            Assert.False(failed.Success);
        }

        var leaked = await auth.LoginAsync("lock.user2", password, "127.0.0.1", "trace-locked");
        Assert.False(leaked.Success);
        Assert.Equal("auth.invalid_credentials", leaked.ErrorKey);

        await _db.UserRepo.UnlockAsync(id);
        var recovered = await auth.LoginAsync("lock.user2", password, "127.0.0.1", "trace-ok");
        Assert.True(recovered.Success);
    }

    [Fact]
    public async Task Non_admin_without_branches_cannot_login()
    {
        const string password = "Str0ng!Pass";
        await CreateIsolatedUserAsync("no.branch", password, withBranch: false);
        var result = await _db.Auth().LoginAsync("no.branch", password, null, "trace-no-branch");
        Assert.False(result.Success);
        Assert.Equal("auth.no_branch", result.ErrorKey);
    }

    private async Task<Guid> CreateIsolatedUserAsync(string username, string password,
        bool withBranch = true)
    {
        var branchId = withBranch
            ? await _db.CreateBranchAsync("B-" + Guid.NewGuid().ToString("N")[..6], "فرع اختبار")
            : Guid.Empty;

        return await _db.UserRepo.CreateAsync(new UserInput
        {
            Username = username,
            Name = username,
            IsActive = true,
            BranchIds = withBranch ? new List<Guid> { branchId } : new List<Guid>(),
            DefaultBranchId = withBranch ? branchId : null
        }, new PasswordHasher().Hash(password));
    }
}
