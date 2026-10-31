using Dapper;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class StandardReportRepository
{
    private async Task<List<T>> QueryAsync<T>(string sql, object parameters, CancellationToken ct)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return (await connection.QueryAsync<T>(new CommandDefinition(sql, parameters,
            cancellationToken: ct))).ToList();
    }

    /// <summary>أعمار المخزون: كل دفعة برصيد وعمرها بالأيام وقيمتها.</summary>
    public async Task<ReportTable> StockAgingAsync(DateOnly asOf, Guid[] branchIds,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT i.code AS Code, i.name_ar AS ItemName, w.name_ar AS Warehouse,
                   l.lot_number AS Lot, l.expiry_date::date AS Expiry, l.received_at::date AS Received,
                   l.quantity_on_hand AS Qty, l.unit_cost AS UnitCost,
                   l.quantity_on_hand * l.unit_cost AS Value,
                   (@asOf::date - l.received_at::date) AS AgeDays
            FROM stock_lots l
            JOIN items i ON i.id = l.item_id
            JOIN warehouses w ON w.id = l.warehouse_id
            WHERE l.is_active AND l.quantity_on_hand > 0
              AND (@allBranches OR EXISTS (SELECT 1 FROM warehouse_branches wb
                     WHERE wb.warehouse_id = l.warehouse_id AND wb.branch_id = ANY(@branchIds)))
            ORDER BY l.received_at
            LIMIT 300
            """;
        var rows = await QueryAsync<StockAgeRow>(sql, new
        {
            asOf = asOf.ToDateTime(TimeOnly.MinValue), branchIds, allBranches = branchIds.Length == 0
        }, ct);
        return new ReportTable(
        [
            new("code", "common.code"), new("name", "common.name_ar"),
            new("warehouse", "report.warehouse"), new("lot", "report.lot"),
            new("received", "report.received", ReportValueType.Date),
            new("expiry", "report.expiry", ReportValueType.Date),
            new("qty", "report.on_hand", ReportValueType.Number),
            new("value", "report.value", ReportValueType.Money),
            new("age", "report.age_days", ReportValueType.Number)
        ],
        rows.Select(r => (IReadOnlyList<string?>)
        [
            r.Code, r.ItemName, r.Warehouse, r.Lot, Fmt(r.Received), Fmt(r.Expiry),
            Num(r.Qty), Money(r.Value), r.AgeDays.ToString()
        ]).ToList(),
        $"report.value_total={Money(rows.Sum(r => r.Value))}");
    }

    /// <summary>حركة المخزون التفصيلية (وارد/صادر) بالقيمة والتكلفة.</summary>
    public async Task<ReportTable> StockMovementsAsync(DateOnly from, DateOnly to, Guid[] branchIds,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT m.movement_date AS MoveDate, i.code AS Code, i.name_ar AS ItemName,
                   w.name_ar AS Warehouse, m.direction AS Direction, m.quantity AS Qty,
                   m.unit_cost AS UnitCost, m.total_cost AS Value
            FROM stock_movements m
            JOIN items i ON i.id = m.item_id
            JOIN warehouses w ON w.id = m.warehouse_id
            WHERE m.movement_date BETWEEN @from AND @to
              AND (@allBranches OR m.branch_id = ANY(@branchIds))
            ORDER BY m.movement_date DESC, m.created_at DESC
            LIMIT 500
            """;
        var rows = await QueryAsync<MovementRow>(sql, new
        {
            from = from.ToDateTime(TimeOnly.MinValue), to = to.ToDateTime(TimeOnly.MinValue),
            branchIds, allBranches = branchIds.Length == 0
        }, ct);
        return new ReportTable(
        [
            new("date", "report.date", ReportValueType.Date), new("code", "common.code"),
            new("name", "common.name_ar"), new("warehouse", "report.warehouse"),
            new("direction", "report.direction"), new("qty", "report.quantity", ReportValueType.Number),
            new("unit_cost", "report.unit_cost", ReportValueType.Money),
            new("value", "report.value", ReportValueType.Money)
        ],
        rows.Select(r => (IReadOnlyList<string?>)
        [
            Fmt(r.MoveDate), r.Code, r.ItemName, r.Warehouse,
            r.Direction == "IN" ? "in" : "out", Num(r.Qty), Money(r.UnitCost), Money(r.Value)
        ]).ToList());
    }

    /// <summary>ملخص الاستلام والصرف لكل مخزن وفرع.</summary>
    public async Task<ReportTable> StockFlowAsync(DateOnly from, DateOnly to, Guid[] branchIds,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT b.name_ar AS BranchName, w.name_ar AS Warehouse,
                   SUM(CASE WHEN m.direction = 'IN' THEN m.quantity ELSE 0 END) AS InQty,
                   SUM(CASE WHEN m.direction = 'OUT' THEN m.quantity ELSE 0 END) AS OutQty,
                   SUM(CASE WHEN m.direction = 'IN' THEN m.total_cost ELSE 0 END) AS InValue,
                   SUM(CASE WHEN m.direction = 'OUT' THEN m.total_cost ELSE 0 END) AS OutValue
            FROM stock_movements m
            JOIN warehouses w ON w.id = m.warehouse_id
            JOIN branches b ON b.id = m.branch_id
            WHERE m.movement_date BETWEEN @from AND @to
              AND (@allBranches OR m.branch_id = ANY(@branchIds))
            GROUP BY b.name_ar, w.name_ar
            ORDER BY b.name_ar, w.name_ar
            """;
        var rows = await QueryAsync<FlowRow>(sql, new
        {
            from = from.ToDateTime(TimeOnly.MinValue), to = to.ToDateTime(TimeOnly.MinValue),
            branchIds, allBranches = branchIds.Length == 0
        }, ct);
        return new ReportTable(
        [
            new("branch", "common.branch"), new("warehouse", "report.warehouse"),
            new("in_qty", "report.in_qty", ReportValueType.Number),
            new("out_qty", "report.out_qty", ReportValueType.Number),
            new("in_value", "report.in_value", ReportValueType.Money),
            new("out_value", "report.out_value", ReportValueType.Money)
        ],
        rows.Select(r => (IReadOnlyList<string?>)
        [
            r.BranchName, r.Warehouse, Num(r.InQty), Num(r.OutQty),
            Money(r.InValue), Money(r.OutValue)
        ]).ToList());
    }

    /// <summary>قيمة المخزون لكل مخزن (مصدر واحد: stock_balances).</summary>
    public async Task<ReportTable> StockValueByWarehouseAsync(Guid[] branchIds,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT w.code AS Code, w.name_ar AS Warehouse, SUM(b.on_hand) AS Qty,
                   SUM(b.inventory_value) AS Value
            FROM stock_balances b
            JOIN warehouses w ON w.id = b.warehouse_id
            WHERE (@allBranches OR EXISTS (SELECT 1 FROM warehouse_branches wb
                     WHERE wb.warehouse_id = b.warehouse_id AND wb.branch_id = ANY(@branchIds)))
            GROUP BY w.code, w.name_ar
            ORDER BY SUM(b.inventory_value) DESC
            """;
        var rows = await QueryAsync<ValueRow>(sql, new
        { branchIds, allBranches = branchIds.Length == 0 }, ct);
        return new ReportTable(
        [
            new("code", "common.code"), new("warehouse", "report.warehouse"),
            new("qty", "report.on_hand", ReportValueType.Number),
            new("value", "report.value", ReportValueType.Money)
        ],
        rows.Select(r => (IReadOnlyList<string?>)[r.Code, r.Warehouse, Num(r.Qty), Money(r.Value)])
            .ToList(), $"report.value_total={Money(rows.Sum(r => r.Value))}");
    }

    private static string? Fmt(DateTime? value) => value?.ToString("yyyy-MM-dd");

    private sealed class StockAgeRow
    {
        public string Code { get; set; } = "";
        public string ItemName { get; set; } = "";
        public string Warehouse { get; set; } = "";
        public string? Lot { get; set; }
        public DateTime? Received { get; set; }
        public DateTime? Expiry { get; set; }
        public decimal Qty { get; set; }
        public decimal Value { get; set; }
        public int AgeDays { get; set; }
    }

    private sealed class MovementRow
    {
        public DateTime MoveDate { get; set; }
        public string Code { get; set; } = "";
        public string ItemName { get; set; } = "";
        public string Warehouse { get; set; } = "";
        public string Direction { get; set; } = "";
        public decimal Qty { get; set; }
        public decimal UnitCost { get; set; }
        public decimal Value { get; set; }
    }

    private sealed class FlowRow
    {
        public string BranchName { get; set; } = "";
        public string Warehouse { get; set; } = "";
        public decimal InQty { get; set; }
        public decimal OutQty { get; set; }
        public decimal InValue { get; set; }
        public decimal OutValue { get; set; }
    }

    private sealed class ValueRow
    {
        public string Code { get; set; } = "";
        public string Warehouse { get; set; } = "";
        public decimal Qty { get; set; }
        public decimal Value { get; set; }
    }
}
