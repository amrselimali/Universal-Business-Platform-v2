using System.Globalization;
using Dapper;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class StandardReportRepository
{
    /// <summary>تسوية الورديات: المفتوح/المتوقع/المعدود والفرق لكل وردية في الفرع.</summary>
    public async Task<ReportTable> ShiftVarianceAsync(DateOnly from, DateOnly to, Guid[] branchIds,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT t.code AS Treasury, s.code AS ShiftCode, s.opened_at AS OpenedAt,
                   s.closed_at AS ClosedAt, s.opening_cash AS Opening, s.expected_cash AS Expected,
                   s.counted_cash AS Counted, s.variance AS Variance, s.status AS Status
            FROM cashier_shifts s
            JOIN treasuries t ON t.id = s.treasury_id
            WHERE s.opened_at::date BETWEEN @from AND @to
              AND (@allBranches OR s.branch_id = ANY(@branchIds))
            ORDER BY s.opened_at DESC
            LIMIT 300
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var rows = (await connection.QueryAsync<ShiftRow>(new CommandDefinition(sql, new
        {
            from = from.ToDateTime(TimeOnly.MinValue), to = to.ToDateTime(TimeOnly.MinValue),
            branchIds, allBranches = branchIds.Length == 0
        }, cancellationToken: ct))).ToList();

        var columns = new List<ReportColumn>
        {
            new("treasury", "report.treasury"),
            new("shift", "report.shift"),
            new("opened", "report.opened_at", ReportValueType.Date),
            new("closed", "report.closed_at", ReportValueType.Date),
            new("opening", "report.opening_cash", ReportValueType.Money),
            new("expected", "report.expected_cash", ReportValueType.Money),
            new("counted", "report.counted_cash", ReportValueType.Money),
            new("variance", "report.variance", ReportValueType.Money),
            new("status", "assets.status")
        };
        var data = rows.Select(r => (IReadOnlyList<string?>)
        [
            r.Treasury, r.ShiftCode, r.OpenedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
            r.ClosedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
            Money(r.Opening), Money(r.Expected), Money(r.Counted), Money(r.Variance), r.Status
        ]).ToList();

        return new ReportTable(columns, data,
            $"report.variance_total={Money(rows.Sum(r => r.Variance))}");
    }

    /// <summary>المخزون الراكد: أصناف لها رصيد ولم تتحرك خلال N يوماً (افتراضي 90).</summary>
    public async Task<ReportTable> DeadStockAsync(DateOnly asOf, Guid[] branchIds, int days = 90,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT i.code AS ItemCode, i.name_ar AS ItemName, w.name_ar AS WarehouseName,
                   b.on_hand AS OnHand, b.inventory_value AS Value, m.last_move AS LastMove,
                   COALESCE(m.last_move::date, i.created_at::date) AS LastActivity
            FROM stock_balances b
            JOIN items i ON i.id = b.item_id
            JOIN warehouses w ON w.id = b.warehouse_id
            LEFT JOIN (SELECT item_id, warehouse_id, MAX(movement_date) AS last_move
                       FROM stock_movements GROUP BY item_id, warehouse_id) m
                   ON m.item_id = b.item_id AND m.warehouse_id = b.warehouse_id
            WHERE b.on_hand > 0
              AND (@allBranches OR EXISTS (SELECT 1 FROM warehouse_branches wb
                     WHERE wb.warehouse_id = b.warehouse_id AND wb.branch_id = ANY(@branchIds)))
              AND COALESCE(m.last_move::date, i.created_at::date) < @asOf::date - @days
            ORDER BY b.inventory_value DESC
            LIMIT 300
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var rows = (await connection.QueryAsync<DeadRow>(new CommandDefinition(sql, new
        {
            asOf = asOf.ToDateTime(TimeOnly.MinValue), days,
            branchIds, allBranches = branchIds.Length == 0
        }, cancellationToken: ct))).ToList();

        var columns = new List<ReportColumn>
        {
            new("code", "common.code"),
            new("name", "common.name_ar"),
            new("warehouse", "report.warehouse"),
            new("on_hand", "report.on_hand", ReportValueType.Number),
            new("value", "report.value", ReportValueType.Money),
            new("last_activity", "report.last_activity", ReportValueType.Date)
        };
        var data = rows.Select(r => (IReadOnlyList<string?>)
        [
            r.ItemCode, r.ItemName, r.WarehouseName, Num(r.OnHand), Money(r.Value),
            r.LastActivity?.ToString("yyyy-MM-dd")
        ]).ToList();

        return new ReportTable(columns, data, $"report.value_total={Money(rows.Sum(r => r.Value))}");
    }

    /// <summary>أداء الموردين: عدد فواتير الشراء وإجمالياتها لكل مورد.</summary>
    public async Task<ReportTable> SupplierPerformanceAsync(DateOnly from, DateOnly to,
        Guid[] branchIds, CancellationToken ct = default)
    {
        const string sql = """
            SELECT s.name_ar AS Supplier, COUNT(DISTINCT d.id)::int AS Docs,
                   SUM(d.subtotal) AS Net, SUM(d.tax_total) AS Tax, SUM(d.total) AS Gross,
                   MAX(d.doc_date) AS LastDoc
            FROM commercial_documents d
            JOIN suppliers s ON s.id = d.supplier_id
            WHERE d.status = 'posted' AND d.doc_type = 'purchase_invoice'
              AND d.doc_date BETWEEN @from AND @to
              AND (@allBranches OR d.branch_id = ANY(@branchIds))
            GROUP BY s.id, s.name_ar
            ORDER BY SUM(d.total) DESC
            LIMIT 200
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var rows = (await connection.QueryAsync<SupplierRow>(new CommandDefinition(sql, new
        {
            from = from.ToDateTime(TimeOnly.MinValue), to = to.ToDateTime(TimeOnly.MinValue),
            branchIds, allBranches = branchIds.Length == 0
        }, cancellationToken: ct))).ToList();

        var columns = new List<ReportColumn>
        {
            new("supplier", "report.supplier"),
            new("docs", "report.documents", ReportValueType.Number),
            new("net", "report.net", ReportValueType.Money),
            new("tax", "report.tax", ReportValueType.Money),
            new("gross", "report.gross", ReportValueType.Money),
            new("last_doc", "report.last_document", ReportValueType.Date)
        };
        var data = rows.Select(r => (IReadOnlyList<string?>)
        [
            r.Supplier, r.Docs.ToString(), Money(r.Net), Money(r.Tax), Money(r.Gross),
            r.LastDoc?.ToString("yyyy-MM-dd")
        ]).ToList();

        return new ReportTable(columns, data, $"report.gross_total={Money(rows.Sum(r => r.Gross))}");
    }

    private sealed class ShiftRow
    {
        public string Treasury { get; set; } = "";
        public string ShiftCode { get; set; } = "";
        public DateTimeOffset OpenedAt { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }
        public decimal Opening { get; set; }
        public decimal Expected { get; set; }
        public decimal Counted { get; set; }
        public decimal Variance { get; set; }
        public string Status { get; set; } = "";
    }

    private sealed class DeadRow
    {
        public string ItemCode { get; set; } = "";
        public string ItemName { get; set; } = "";
        public string WarehouseName { get; set; } = "";
        public decimal OnHand { get; set; }
        public decimal Value { get; set; }
        public DateTime? LastActivity { get; set; }
    }

    private sealed class SupplierRow
    {
        public string Supplier { get; set; } = "";
        public int Docs { get; set; }
        public decimal Net { get; set; }
        public decimal Tax { get; set; }
        public decimal Gross { get; set; }
        public DateTime? LastDoc { get; set; }
    }
}
