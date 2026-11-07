using Ubp.Core.Models;
using Ubp.Core.Security;
using Xunit;

namespace Ubp.Tests;

/// <summary>قوائم الموظفين للاختيار تعرض موظفي فرع المستخدم + موظفي الشركة فقط (0094).</summary>
public sealed class EmployeeOptionScopeTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public EmployeeOptionScopeTests(TestDb db) => _db = db;

    [Fact]
    public async Task Options_shows_only_user_branch_employees_plus_company_employees()
    {
        var admin = await _db.AdminActorAsync();
        var branchA = await _db.CreateBranchAsync("EO-B-A", "فرع موظفين أ");
        var branchB = await _db.CreateBranchAsync("EO-B-B", "فرع موظفين ب");

        var inA = Guid.CreateVersion7();
        await _db.EmployeeRepo.CreateAsync(inA, new EmployeeInput
        { Code = "EO-E-A", NameAr = "موظف فرع أ", IsServiceExecutor = true, BranchId = branchA },
            admin.UserId);
        var inB = Guid.CreateVersion7();
        await _db.EmployeeRepo.CreateAsync(inB, new EmployeeInput
        { Code = "EO-E-B", NameAr = "موظف فرع ب", IsServiceExecutor = true, BranchId = branchB },
            admin.UserId);
        var company = Guid.CreateVersion7();
        await _db.EmployeeRepo.CreateAsync(company, new EmployeeInput
        { Code = "EO-E-C", NameAr = "موظف الشركة", IsServiceExecutor = true }, admin.UserId);

        var userId = await _db.CreateUserAsync("eo-user", "Passw0rd!", new[] { branchA });
        var limited = new ActorContext
        {
            UserId = userId, Username = "eo-user", IsAdmin = true, Language = "ar",
            AllowedBranchIds = new[] { branchA }, AllBranchIds = new[] { branchA }, TraceId = "t"
        };

        var options = await _db.MasterData().EmployeeOptionsAsync(limited);
        Assert.Contains(options, e => e.Id == inA);
        Assert.Contains(options, e => e.Id == company);
        Assert.DoesNotContain(options, e => e.Id == inB);
    }
}