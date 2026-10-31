using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IBiRepository
{
    /// <summary>تحديث متزامن بالكامل (يُستخدم داخل الاختبارات/المهام اليدوية).</summary>
    Task RefreshAsync(CancellationToken ct = default);
    /// <summary>تحديث CONCURRENTLY لكل عرض (يتطلب فهرساً فريداً؛ لا يعمل داخل معاملة).</summary>
    Task RefreshConcurrentlyAsync(CancellationToken ct = default);
    Task<IReadOnlyList<BiSalesDailyRow>> SalesDailyAsync(DateOnly from, DateOnly to,
        Guid? branchId = null, CancellationToken ct = default);
    Task<IReadOnlyList<BiStockValueRow>> StockValueAsync(CancellationToken ct = default);
}

/// <summary>
/// قراءة/تحديث عروض مخطط <c>bi</c>. القراءة للعروض المادية فقط،
/// والكتابة التنشغيلية لا تلمس هذا المخطط (يُحدَّث بالتحديث المجدول أو المهمة اليدوية).
/// </summary>
public sealed class BiRepository : IBiRepository
{
    private static readonly string[] ConcurrentRefreshes =
    [
        "REFRESH MATERIALIZED VIEW CONCURRENTLY bi.sales_daily",
        "REFRESH MATERIALIZED VIEW CONCURRENTLY bi.stock_value_by_warehouse",
        "REFRESH MATERIALIZED VIEW CONCURRENTLY bi.party_balances_monthly"
    ];

    private readonly IDbConnectionFactory _factory;

    public BiRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("SELECT bi.fn_refresh_all()",
            cancellationToken: ct));
    }

    public async Task RefreshConcurrentlyAsync(CancellationToken ct = default)
    {
        // كل جملة في معاملة ضمنية مستقلة: CONCURRENTLY لا يعمل داخل معاملة صريحة.
        await using var connection = await _factory.OpenAsync(ct);
        foreach (var sql in ConcurrentRefreshes)
            await connection.ExecuteAsync(new CommandDefinition(sql, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<BiSalesDailyRow>> SalesDailyAsync(DateOnly from, DateOnly to,
        Guid? branchId = null, CancellationToken ct = default)
    {
        const string sql = """
            SELECT day AS Day, branch_id AS BranchId, invoice_count AS InvoiceCount,
                   return_count AS ReturnCount, net_total AS NetTotal, tax_total AS TaxTotal,
                   gross_total AS GrossTotal, cogs_total AS CogsTotal
            FROM bi.sales_daily
            WHERE day BETWEEN @from AND @to AND (@branchId IS NULL OR branch_id = @branchId)
            ORDER BY day, branch_id
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<BiSalesDailyRow>(new CommandDefinition(sql,
            new { from, to, branchId }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<BiStockValueRow>> StockValueAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT warehouse_id AS WarehouseId, warehouse_code AS WarehouseCode,
                   warehouse_name AS WarehouseName, on_hand AS OnHand,
                   inventory_value AS InventoryValue
            FROM bi.stock_value_by_warehouse
            ORDER BY warehouse_code
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<BiStockValueRow>(new CommandDefinition(sql,
            cancellationToken: ct));
        return rows.ToList();
    }
}
