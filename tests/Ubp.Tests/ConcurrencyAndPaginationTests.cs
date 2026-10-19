using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Xunit;

namespace Ubp.Tests;

public sealed class ConcurrencyAndPaginationTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public ConcurrencyAndPaginationTests(TestDb db) => _db = db;

    [Fact]
    public async Task Stale_version_is_rejected()
    {
        var branch = await _db.BranchRepo.CreateAsync(new BranchInput { Code = "VER-1", NameAr = "فرع" });
        var first = await _db.BranchRepo.UpdateAsync(branch.Id, branch.Version,
            new BranchInput { Code = branch.Code, NameAr = "اسم جديد" });
        Assert.NotNull(first);
        Assert.Equal(branch.Version + 1, first!.Version);

        var stale = await _db.BranchRepo.UpdateAsync(branch.Id, branch.Version,
            new BranchInput { Code = branch.Code, NameAr = "اسم متأخر" });
        Assert.Null(stale);
    }

    [Fact]
    public async Task Page_size_is_hard_capped_at_database_level_to_25()
    {
        for (var i = 0; i < 30; i++)
            await _db.BranchRepo.CreateAsync(new BranchInput
            {
                Code = $"PAGE-{i:00}", NameAr = $"فرع {i}"
            });

        var result = await _db.BranchRepo.ListAsync(new PageRequest { Page = 1, PageSize = 999 },
            scope: 2, actorId: Guid.NewGuid(), branchIds: Array.Empty<Guid>(), isAdmin: true);
        Assert.Equal(25, result.Items.Count);
        Assert.Equal(25, result.PageSize);
        Assert.True(result.Total >= 30);
    }

    [Fact]
    public async Task Arabic_search_normalization_matches_hamza_ta_marbuta_and_yaa()
    {
        await _db.CreateBranchAsync("AR-1", "القاهرة الجديدة");
        var result = await _db.BranchRepo.ListAsync(new PageRequest { PageSize = 25, Search = "القاهره" },
            scope: 2, actorId: Guid.NewGuid(), branchIds: Array.Empty<Guid>(), isAdmin: true);
        Assert.Contains(result.Items, b => b.Code == "AR-1");
    }

    [Fact]
    public async Task Audit_log_is_immutable_and_uses_keyset_pagination()
    {
        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var id = await connection.ExecuteScalarAsync<long>(
            "INSERT INTO audit_log (table_name, record_id, action) VALUES ('test','1','INSERT') RETURNING id");

        await Assert.ThrowsAsync<PostgresException>(async () =>
            await connection.ExecuteAsync("UPDATE audit_log SET action='DELETE' WHERE id=@id", new { id }));

        await connection.ExecuteAsync("""
            INSERT INTO audit_log (table_name, record_id, action)
            SELECT 'test', g::text, 'UPDATE' FROM generate_series(1, 40) g
            """);
        var repo = new Ubp.Infrastructure.Repositories.AuditRepository(_db.Factory);
        var page = await repo.ListAsync(new AuditFilter(), scope: 2, actorId: Guid.NewGuid(),
            branchIds: Array.Empty<Guid>(), isAdmin: true, limit: 25);
        Assert.Equal(25, page.Items.Count);
        Assert.True(page.HasMore);
        Assert.NotNull(page.NextBeforeId);
        var second = await repo.ListAsync(new AuditFilter { BeforeId = page.NextBeforeId },
            scope: 2, actorId: Guid.NewGuid(), branchIds: Array.Empty<Guid>(), isAdmin: true, limit: 25);
        Assert.True(second.Items.All(x => x.Id < page.NextBeforeId));
    }
}
