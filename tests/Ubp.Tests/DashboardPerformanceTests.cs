using System.Diagnostics;
using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Infrastructure.Repositories;
using Xunit;

namespace Ubp.Tests;

/// <summary>
/// بوابة أداء 6C: لوحة المؤشرات تقرأ من الإجماليات الجاهزة فقط،
/// فيبقى زمنها أقل من 300 مللي ثانية مهما كبر عدد الأيام أو الفروع.
/// </summary>
public sealed class DashboardPerformanceTests : IClassFixture<TestDb>
{
    private const int Days = 180;
    private const int Branches = 3;
    private const int BudgetMilliseconds = 300;

    private readonly TestDb _db;

    public DashboardPerformanceTests(TestDb db) => _db = db;

    [Fact]
    public async Task Dashboard_snapshot_stays_under_the_300ms_budget()
    {
        var actor = await _db.AdminActorAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var expected = await SeedTotalsAsync(monthStart);

        var repository = new DashboardRepository(_db.Factory);

        // تسخين البركة والخطة، ثم قياس فعلي.
        await repository.GetAsync(null, today, monthStart);

        var watch = Stopwatch.StartNew();
        var snapshot = await repository.GetAsync(null, today, monthStart);
        watch.Stop();

        Assert.True(watch.ElapsedMilliseconds < BudgetMilliseconds,
            $"dashboard read took {watch.ElapsedMilliseconds} ms (> {BudgetMilliseconds})");
        Assert.Equal(expected.Net, snapshot.Month.NetTotal);
        Assert.Equal(expected.Invoices, snapshot.Month.InvoiceCount);

        var scoped = await _db.Dashboard().GetAsync(actor);
        Assert.True(scoped.Month.NetTotal >= 0m);
    }

    private async Task<(decimal Net, int Invoices)> SeedTotalsAsync(DateOnly monthStart)
    {
        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        var branchIds = new List<Guid>();
        for (var i = 0; i < Branches; i++)
            branchIds.Add(await _db.CreateBranchAsync("PERF-B" + i, "فرع أداء " + i));

        var today = DateTime.UtcNow.Date;
        foreach (var branchId in branchIds)
        {
            for (var day = 0; day < Days; day++)
            {
                await connection.ExecuteAsync("""
                    INSERT INTO sales_daily_totals
                        (day, branch_id, invoice_count, net_total, tax_total, gross_total, cogs_total)
                    VALUES (@day, @branch, 1, 10, 1, 11, 4)
                    ON CONFLICT (day, branch_id) DO UPDATE SET net_total = 10, invoice_count = 1,
                        tax_total = 1, gross_total = 11, cogs_total = 4
                    """, new { day = today.AddDays(-day), branch = branchId });
            }
        }

        var net = await connection.ExecuteScalarAsync<decimal>(
            "SELECT COALESCE(SUM(net_total), 0) FROM sales_daily_totals WHERE day >= @from",
            new { from = monthStart.ToDateTime(TimeOnly.MinValue) });
        var invoices = await connection.ExecuteScalarAsync<int>(
            "SELECT COALESCE(SUM(invoice_count), 0)::int FROM sales_daily_totals WHERE day >= @from",
            new { from = monthStart.ToDateTime(TimeOnly.MinValue) });
        Assert.True(invoices > 0);
        return (net, invoices);
    }
}
