using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Xunit;

namespace Ubp.Tests;

/// <summary>ربط الموظف بفرع (أو الشركة) وحفظه، مع منع فرع خارج نطاق المستخدم.</summary>
public sealed class EmployeeBranchTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public EmployeeBranchTests(TestDb db) => _db = db;

    [Fact]
    public async Task Employee_branch_is_saved_and_null_means_company()
    {
        var actor = await _db.AdminActorAsync();
        var branch = await _db.CreateBranchAsync("EB-B-1", "فرع الموظف");
        var svc = _db.MasterData();

        var withBranch = await svc.SaveEmployeeAsync(actor, null, 0, new EmployeeInput
        {
            Code = "EB-E-1", NameAr = "موظف بفرع", IsServiceExecutor = true, BranchId = branch
        });
        Assert.Equal(branch, withBranch.BranchId);
        Assert.Equal("فرع الموظف", withBranch.BranchName);

        var company = await svc.SaveEmployeeAsync(actor, null, 0, new EmployeeInput
        {
            Code = "EB-E-2", NameAr = "موظف الشركة", IsServiceExecutor = true
        });
        Assert.Null(company.BranchId);
    }

    [Fact]
    public async Task Employee_cannot_be_linked_to_a_foreign_branch()
    {
        var admin = await _db.AdminActorAsync();
        var allowed = await _db.CreateBranchAsync("EB-B-2", "فرع مسموح");
        var foreign = await _db.CreateBranchAsync("EB-B-3", "فرع أجنبي");
        var userId = await _db.CreateUserAsync("eb-user", "Passw0rd!", new[] { allowed });
        var limited = new ActorContext
        {
            UserId = userId, Username = "eb-user", IsAdmin = true, Language = "ar",
            AllowedBranchIds = new[] { allowed }, TraceId = "test"
        };

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _db.MasterData().SaveEmployeeAsync(limited, null, 0, new EmployeeInput
            {
                Code = "EB-E-3", NameAr = "موظف أجنبي", IsServiceExecutor = true,
                BranchId = foreign
            }));
        _ = admin;
    }
}