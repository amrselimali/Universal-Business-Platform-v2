using Dapper;
using Npgsql;
using Xunit;

namespace Ubp.Tests;

public sealed class WarehouseConsistencyTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public WarehouseConsistencyTests(TestDb db) => _db = db;

    [Fact]
    public async Task Removing_the_only_branch_covering_a_user_warehouse_is_rejected()
    {
        var branch = await _db.CreateBranchAsync("COV-1", "فرع تغطية");
        var warehouse = await _db.CreateWarehouseAsync("COV-WH", "مخزن تغطية", branch);
        var userId = await _db.CreateUserAsync("cov.user", "Str0ng!Pass",
            new[] { branch }, new[] { warehouse });

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var exception = await Assert.ThrowsAsync<PostgresException>(async () =>
            await connection.ExecuteAsync(
                "DELETE FROM user_branches WHERE user_id = @userId AND branch_id = @branch",
                new { userId, branch }));
        Assert.Contains("USER_WAREHOUSE_COVERAGE", exception.MessageText);
    }

    [Fact]
    public async Task User_service_rejects_a_warehouse_outside_the_users_branches()
    {
        var branchA = await _db.CreateBranchAsync("COV-2A", "فرع أ");
        var branchB = await _db.CreateBranchAsync("COV-2B", "فرع ب");
        var warehouseB = await _db.CreateWarehouseAsync("COV-2WH", "مخزن ب", branchB);
        var admin = await _db.AdminActorAsync();

        var exception = await Assert.ThrowsAsync<Ubp.Core.Results.ValidationAppException>(async () =>
            await _db.UserService().CreateAsync(admin, new Ubp.Core.Models.UserInput
            {
                Username = "cov2.user", Name = "cov2", Password = "Str0ng!Pass",
                BranchIds = new List<Guid> { branchA },
                WarehouseIds = new List<Guid> { warehouseB }
            }));
        Assert.Equal("validation.warehouse_not_in_branches", exception.Code);
    }
}
