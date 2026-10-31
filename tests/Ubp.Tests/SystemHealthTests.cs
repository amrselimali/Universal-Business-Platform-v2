using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Infrastructure.Repositories;
using Xunit;

namespace Ubp.Tests;

/// <summary>
/// المرحلة 7 (7B/7C): سجل النسخ الاحتياطي يُسجَّل من وظيفة خارجية ويُقرأ فقط،
/// وشاشة صحة النظام تعرض حقائق القاعدة وحالة الترحيلات.
/// </summary>
public sealed class SystemHealthTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public SystemHealthTests(TestDb db) => _db = db;

    private BackupLogRepository Backups() => new(_db.Factory);

    [Fact]
    public async Task Recording_a_backup_shows_it_in_the_log()
    {
        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var id = await connection.ExecuteScalarAsync<Guid>("""
            SELECT fn_record_backup('SUCCESS', 'FULL', 'ubp-test.dump', 1048576, 42)
            """);

        var last = await Backups().LastAsync();
        Assert.NotNull(last);
        Assert.Equal(BackupStatuses.Success, last!.Status);
        Assert.Equal("ubp-test.dump", last.FileName);
        Assert.Equal(1_048_576, last.SizeBytes);
        Assert.Equal(42, last.DurationSeconds);
        Assert.NotNull(last.FinishedAt);

        var list = await Backups().ListAsync();
        Assert.Contains(list, b => b.Id == id);
    }

    [Fact]
    public async Task Run_backup_row_cannot_be_recorded_without_a_finish_time()
    {
        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();

        var error = await Assert.ThrowsAsync<PostgresException>(async () =>
            await connection.ExecuteAsync("""
                INSERT INTO backup_log (status, kind, file_name) VALUES ('SUCCESS', 'FULL', 'x.dump')
                """));
        Assert.Equal("23514", error.SqlState);
    }

    [Fact]
    public async Task Snapshot_reports_database_facts_and_migration_state()
    {
        var actor = await _db.AdminActorAsync();
        var snapshot = await _db.SystemHealth().GetAsync(actor);

        Assert.True(snapshot.Facts.DatabaseOk, snapshot.Facts.Error);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.Facts.ServerVersion));
        Assert.True(snapshot.Facts.DatabaseSizeBytes > 0);
        Assert.True(snapshot.Facts.TableCount > 50);
        Assert.True(snapshot.AppliedMigrations >= 43, $"applied={snapshot.AppliedMigrations}");
        Assert.Equal(0, snapshot.PendingMigrations);
        Assert.Empty(snapshot.PendingScripts);
    }

    [Fact]
    public async Task Backup_list_is_ordered_by_most_recent_first()
    {
        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        await connection.ExecuteAsync("SELECT fn_record_backup('FAILED', 'FULL', 'old.dump', 0, 1, 'disk full')");

        var list = await Backups().ListAsync(5);
        Assert.NotEmpty(list);
        for (var i = 1; i < list.Count; i++)
            Assert.True(list[i - 1].StartedAt >= list[i].StartedAt);
    }
}
