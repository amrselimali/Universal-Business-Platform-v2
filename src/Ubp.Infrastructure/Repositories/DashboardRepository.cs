using Dapper;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IDashboardRepository
{
    /// <summary>لقطة اللوحة من جدول الإجماليات اليومية (استعلامات ثابتة الزمن).</summary>
    Task<DashboardSnapshot> GetAsync(Guid? branchId, DateOnly today, DateOnly monthStart,
        CancellationToken ct = default);
}

/// <summary>
/// قراءة لوحة المؤشرات: تجميع من <c>sales_daily_totals</c> + قيمة المخزون + ذمم العملاء.
/// لا يلمس جداول الفواتير أو القيود، فيبقى الزمن ثابتاً مع نمو البيانات.
/// </summary>
public sealed class DashboardRepository : IDashboardRepository
{
    private readonly IDbConnectionFactory _factory;

    public DashboardRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<DashboardSnapshot> GetAsync(Guid? branchId, DateOnly today,
        DateOnly monthStart, CancellationToken ct = default)
    {
        const string salesSql = """
            SELECT
                COALESCE(SUM(net_total)   FILTER (WHERE day = @today), 0)  AS TodayNet,
                COALESCE(SUM(tax_total)   FILTER (WHERE day = @today), 0)  AS TodayTax,
                COALESCE(SUM(gross_total) FILTER (WHERE day = @today), 0)  AS TodayGross,
                COALESCE(SUM(cogs_total)  FILTER (WHERE day = @today), 0)  AS TodayCogs,
                COALESCE(SUM(invoice_count) FILTER (WHERE day = @today), 0)::int AS TodayInvoices,
                COALESCE(SUM(return_count)  FILTER (WHERE day = @today), 0)::int AS TodayReturns,
                COALESCE(SUM(net_total), 0)   AS MonthNet,
                COALESCE(SUM(tax_total), 0)   AS MonthTax,
                COALESCE(SUM(gross_total), 0) AS MonthGross,
                COALESCE(SUM(cogs_total), 0)  AS MonthCogs,
                COALESCE(SUM(invoice_count), 0)::int AS MonthInvoices,
                COALESCE(SUM(return_count), 0)::int  AS MonthReturns
            FROM sales_daily_totals
            WHERE day >= @monthStart AND (@branchId IS NULL OR branch_id = @branchId)
            """;

        const string stockSql = """
            SELECT COALESCE(SUM(b.inventory_value), 0)
            FROM stock_balances b
            WHERE @branchId IS NULL OR EXISTS (
                SELECT 1 FROM warehouse_branches wb
                WHERE wb.warehouse_id = b.warehouse_id AND wb.branch_id = @branchId)
            """;

        const string receivablesSql = """
            SELECT COALESCE(SUM(pb.debit - pb.credit), 0)
            FROM party_balances pb
            WHERE pb.party_type = 'C'
              AND (@branchId IS NULL OR pb.branch_id = @branchId)
            """;

        await using var connection = await _factory.OpenAsync(ct);
        var row = await connection.QuerySingleAsync<SalesRow>(
            new CommandDefinition(salesSql, new { today, monthStart, branchId }, cancellationToken: ct));
        var stockValue = await connection.ExecuteScalarAsync<decimal>(
            new CommandDefinition(stockSql, new { branchId }, cancellationToken: ct));
        var receivables = await connection.ExecuteScalarAsync<decimal>(
            new CommandDefinition(receivablesSql, new { branchId }, cancellationToken: ct));

        return new DashboardSnapshot(
            new SalesKpi(row.TodayNet, row.TodayTax, row.TodayGross, row.TodayCogs,
                row.TodayInvoices, row.TodayReturns),
            new SalesKpi(row.MonthNet, row.MonthTax, row.MonthGross, row.MonthCogs,
                row.MonthInvoices, row.MonthReturns),
            stockValue, receivables);
    }

    private sealed class SalesRow
    {
        public decimal TodayNet { get; set; }
        public decimal TodayTax { get; set; }
        public decimal TodayGross { get; set; }
        public decimal TodayCogs { get; set; }
        public int TodayInvoices { get; set; }
        public int TodayReturns { get; set; }
        public decimal MonthNet { get; set; }
        public decimal MonthTax { get; set; }
        public decimal MonthGross { get; set; }
        public decimal MonthCogs { get; set; }
        public int MonthInvoices { get; set; }
        public int MonthReturns { get; set; }
    }
}
