using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Xunit;

namespace Ubp.Tests;

/// <summary>
/// 7D: أداة الاستيراد — المعاينة لا تكتب شيئاً، والتنفيذ لا يكتب عند وجود خطأ،
/// والنجاح يحفظ قيد مسودة ويُسجَّل في import_jobs.
/// </summary>
public sealed class ImportToolTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public ImportToolTests(TestDb db) => _db = db;

    private async Task<(string Debit, string Credit)> CodesAsync()
    {
        await _db.SeedAccountingAsync();
        // نفس مصدر خدمة الاستيراد: الحسابات القابلة للترحيل فقط.
        var postable = (await _db.AccountRepo.ListPostableAsync())
            .Where(a => !a.RequiresParty)
            .OrderBy(a => a.Code)
            .ToList();
        Assert.True(postable.Count >= 2, "need two postable accounts without party requirement");
        return (postable[0].Code, postable[1].Code);
    }

    private static string Csv(string debit, string credit, string? badCode = null,
        int creditAmount = 100) =>
        "code,debit,credit,description\n" +
        $"{badCode ?? debit},100,0,opening\n" +
        $"{credit},0,{creditAmount},revenue";

    private async Task<(Guid Branch, int Entries, int Jobs)> SnapshotAsync()
    {
        var branch = await _db.CreateBranchAsync("IMP-" + Guid.NewGuid().ToString("N")[..6],
            "فرع الاستيراد");
        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var entries = await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM journal_entries");
        var jobs = await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM import_jobs");
        return (branch, entries, jobs);
    }

    [Fact]
    public async Task Admin_sees_the_import_screen_permissions()
    {
        await _db.SeedAccountingAsync();
        var actor = await _db.AdminActorAsync();
        Assert.True(await _db.Permissions().HasAsync(actor, ScreenCodes.ImportTool,
            ActionCodes.Preview));
        Assert.True(await _db.Permissions().HasAsync(actor, ScreenCodes.ImportTool,
            ActionCodes.ImportCsv));
    }

    [Fact]
    public async Task Preview_reports_errors_and_writes_nothing()
    {
        await _db.SeedAccountingAsync();
        var (debit, credit) = await CodesAsync();
        var actor = await _db.AdminActorAsync();
        var (_, entriesBefore, jobsBefore) = await SnapshotAsync();

        var result = await _db.Imports().PreviewAsync(actor, Csv(debit, credit, "NOPE"));

        Assert.NotEmpty(result.Errors);
        Assert.Equal("validation.account_not_found", result.Errors[0].Key);

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        Assert.Equal(entriesBefore, await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM journal_entries"));
        Assert.Equal(jobsBefore, await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM import_jobs"));
    }

    [Fact]
    public async Task Execute_with_errors_records_a_failed_job_and_writes_no_entry()
    {
        await _db.SeedAccountingAsync();
        var (debit, credit) = await CodesAsync();
        var actor = await _db.AdminActorAsync();
        var (branch, entriesBefore, _) = await SnapshotAsync();

        var outcome = await _db.Imports().ExecuteAsync(actor, Csv(debit, credit, "NOPE"),
            "broken.csv", branch, DateTime.Today);

        Assert.False(outcome.Success);
        Assert.Null(outcome.JournalEntryId);
        Assert.Equal(1, outcome.RowsFailed);

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        Assert.Equal(entriesBefore, await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM journal_entries"));
        Assert.Equal(ImportJobStatuses.Failed, await connection.ExecuteScalarAsync<string>(
            "SELECT status FROM import_jobs WHERE id = @id", new { id = outcome.JobId }));
    }

    [Fact]
    public async Task Unbalanced_csv_is_rejected_without_writing()
    {
        await _db.SeedAccountingAsync();
        var (debit, credit) = await CodesAsync();
        var actor = await _db.AdminActorAsync();
        var (branch, entriesBefore, _) = await SnapshotAsync();

        var outcome = await _db.Imports().ExecuteAsync(actor,
            Csv(debit, credit, creditAmount: 90), "unbalanced.csv", branch, DateTime.Today);

        Assert.False(outcome.Success);
        Assert.Contains("csv_not_balanced", outcome.ErrorSummary);

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        Assert.Equal(entriesBefore, await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM journal_entries"));
    }

    [Fact]
    public async Task Execute_balanced_csv_saves_a_draft_entry_and_an_imported_job()
    {
        await _db.SeedAccountingAsync();
        var (debit, credit) = await CodesAsync();
        var actor = await _db.AdminActorAsync();
        var (branch, entriesBefore, _) = await SnapshotAsync();

        var outcome = await _db.Imports().ExecuteAsync(actor, Csv(debit, credit), "opening.csv",
            branch, DateTime.Today);

        Assert.True(outcome.Success, outcome.ErrorSummary);
        Assert.NotNull(outcome.JournalEntryId);
        Assert.Equal(2, outcome.RowsImported);

        var jobs = await _db.Imports().JobsAsync(actor, 5);
        var job = jobs.Single(j => j.Id == outcome.JobId);
        Assert.Equal(ImportJobStatuses.Imported, job.Status);
        Assert.Equal(100m, job.TotalDebit);
        Assert.Equal(100m, job.TotalCredit);
        Assert.Equal("opening.csv", job.FileName);

        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        Assert.Equal(entriesBefore + 1, await connection.ExecuteScalarAsync<int>(
            "SELECT count(*)::int FROM journal_entries"));
    }
}
