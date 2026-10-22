using System.Diagnostics;
using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Xunit;
using Xunit.Abstractions;

namespace Ubp.Tests;

/// <summary>
/// اختبار أداء اختياري: يُفعّل بضبط UBP_LOAD_ROWS (مثلاً 1000000).
/// يتحقق أن ميزان المراجعة وكشف الأستاذ يبقيان تحت 0.5 ثانية على بيانات ضخمة.
/// </summary>
public sealed class AccountingPerformanceTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;
    private readonly ITestOutputHelper _output;

    public AccountingPerformanceTests(TestDb db, ITestOutputHelper output)
    {
        _db = db;
        _output = output;
    }

    [Fact]
    public async Task Trial_balance_and_ledger_stay_under_half_a_second_on_bulk_data()
    {
        if (!int.TryParse(Environment.GetEnvironmentVariable("UBP_LOAD_ROWS"), out var rows) || rows < 1000)
            return;

        var actor = await _db.AdminActorAsync();
        var chart = await _db.SeedAccountingAsync();
        var branch = await _db.CreateBranchAsync("GL-PERF", "فرع الأداء");
        var journalId = await _db.JournalIdByCodeAsync("GEN");

        var entryId = Guid.CreateVersion7();
        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();

        await connection.ExecuteAsync("""
            INSERT INTO journal_entries
                (id, journal_id, branch_id, entry_no, entry_date, description, status,
                 source_type, total_debit, total_credit, created_by)
            VALUES (@entryId, @journalId, @branch, 'PERF-1', '2026-06-15', 'حمولة أداء',
                    'draft', 'manual', 0, 0, @userId)
            """, new { entryId, journalId, branch, userId = actor.UserId });

        await connection.ExecuteAsync("""
            INSERT INTO journal_entry_lines
                (id, entry_id, line_no, account_id, debit, credit, branch_id, entry_date)
            SELECT gen_random_uuid(), @entryId, g,
                   CASE WHEN g % 2 = 1 THEN @debitAccount ELSE @creditAccount END,
                   CASE WHEN g % 2 = 1 THEN 1 ELSE 0 END,
                   CASE WHEN g % 2 = 1 THEN 0 ELSE 1 END,
                   @branch,
                   (date '2025-07-01' + ((g % 365)::int))::date
            FROM generate_series(1, @rows) AS g
            """, new
        {
            entryId, rows, branch, debitAccount = chart["1101"].Id,
            creditAccount = chart["4100"].Id
        });

        await connection.ExecuteAsync("SELECT fn_gl_post_entry(@entryId, @userId)",
            new { entryId, userId = actor.UserId });

        var reports = _db.AccountingReports();

        var stopwatch = Stopwatch.StartNew();
        var trial = await reports.TrialBalanceAsync(actor, new DateTime(2025, 1, 1),
            new DateTime(2026, 12, 31), branch);
        stopwatch.Stop();
        Assert.True(trial.IsBalanced);
        _output.WriteLine($"TrialBalance: {stopwatch.ElapsedMilliseconds}ms on {rows:N0} lines");
        Assert.True(stopwatch.ElapsedMilliseconds < 500,
            $"ميزان المراجعة استغرق {stopwatch.ElapsedMilliseconds}ms على {rows} سطر");

        stopwatch.Restart();
        var ledger = await reports.LedgerAsync(actor, new LedgerRequest
        {
            AccountId = chart["1101"].Id, BranchId = branch,
            From = new DateTime(2025, 7, 1), To = new DateTime(2026, 6, 30),
            IncludeChildren = false, PageSize = 50
        });
        stopwatch.Stop();
        Assert.True(ledger.Rows.Count > 0);
        _output.WriteLine($"Ledger: {stopwatch.ElapsedMilliseconds}ms on {rows:N0} lines");
        Assert.True(stopwatch.ElapsedMilliseconds < 500,
            $"كشف الأستاذ استغرق {stopwatch.ElapsedMilliseconds}ms على {rows} سطر");
    }
}
