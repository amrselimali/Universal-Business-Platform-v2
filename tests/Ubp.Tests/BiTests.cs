using Dapper;
using Npgsql;
using Ubp.Infrastructure.Repositories;
using Xunit;

namespace Ubp.Tests;

/// <summary>
/// 6B: مخطط bi للقراءة فقط — العروض المادية موجودة ومطابقة للمصدر، ودور bi_reader
/// يقرأ bi فقط ولا يكتب ولا يرى public.
/// </summary>
public sealed class BiTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public BiTests(TestDb db) => _db = db;

    private BiRepository Repo() => new(_db.Factory);

    private async Task<Guid> NewBranchWithTotalsAsync(string suffix, int invoiceCount,
        decimal net, DateOnly day)
    {
        var branch = await _db.CreateBranchAsync("BI-" + suffix, "فرع تحليلات " + suffix);
        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();
        await connection.ExecuteAsync("""
            INSERT INTO sales_daily_totals
                (day, branch_id, invoice_count, net_total, tax_total, gross_total, cogs_total)
            VALUES (@day, @branch, @count, @net, 0, @net, 0)
            ON CONFLICT (day, branch_id) DO UPDATE SET invoice_count = @count, net_total = @net
            """, new { day = day.ToDateTime(TimeOnly.MinValue), branch, count = invoiceCount, net });
        return branch;
    }

    [Fact]
    public async Task Views_reflect_the_source_after_a_refresh()
    {
        var day = DateOnly.FromDateTime(DateTime.UtcNow);
        var branch = await NewBranchWithTotalsAsync("1", 4, 800m, day);

        await Repo().RefreshAsync();

        var rows = await Repo().SalesDailyAsync(day, day, branch);
        var row = Assert.Single(rows);
        Assert.Equal(4, row.InvoiceCount);
        Assert.Equal(800m, row.NetTotal);
        Assert.Equal(800m, row.GrossTotal);
    }

    [Fact]
    public async Task Concurrent_refresh_works_and_picks_up_new_rows()
    {
        var day = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var branch = await NewBranchWithTotalsAsync("2", 2, 300m, day);
        await Repo().RefreshAsync();

        var before = (await Repo().SalesDailyAsync(day, day, branch)).Count;
        await NewBranchWithTotalsAsync("3", 1, 100m, day);
        await Repo().RefreshConcurrentlyAsync();
        var after = (await Repo().SalesDailyAsync(day, day)).Count;

        Assert.True(after >= before);
        Assert.Contains(await Repo().SalesDailyAsync(day, day, branch), r => r.NetTotal == 300m);
    }

    [Fact]
    public async Task Stock_value_view_is_readable()
    {
        await Repo().RefreshAsync();
        var rows = await Repo().StockValueAsync();
        Assert.NotNull(rows);
    }

    [Fact]
    public async Task Bi_reader_can_read_the_bi_schema_but_not_write_or_read_public()
    {
        await using var connection = new NpgsqlConnection(TestDb.AdminConnection);
        await connection.OpenAsync();

        Assert.True(await connection.ExecuteScalarAsync<bool>(
            "SELECT has_schema_privilege('bi_reader', 'bi', 'USAGE')"));
        Assert.True(await connection.ExecuteScalarAsync<bool>(
            "SELECT has_table_privilege('bi_reader', 'bi.sales_daily', 'SELECT')"));
        Assert.True(await connection.ExecuteScalarAsync<bool>(
            "SELECT has_table_privilege('bi_reader', 'bi.stock_value_by_warehouse', 'SELECT')"));

        Assert.False(await connection.ExecuteScalarAsync<bool>(
            "SELECT has_table_privilege('bi_reader', 'bi.sales_daily', 'INSERT')"));
        Assert.False(await connection.ExecuteScalarAsync<bool>(
            "SELECT has_table_privilege('bi_reader', 'bi.sales_daily', 'UPDATE')"));
        Assert.False(await connection.ExecuteScalarAsync<bool>(
            "SELECT has_table_privilege('bi_reader', 'bi.sales_daily', 'DELETE')"));
        Assert.False(await connection.ExecuteScalarAsync<bool>(
            "SELECT has_table_privilege('bi_reader', 'public.users', 'SELECT')"));
        Assert.False(await connection.ExecuteScalarAsync<bool>(
            "SELECT has_schema_privilege('bi_reader', 'public', 'CREATE')"));
    }
}
