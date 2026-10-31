using System.Globalization;
using Dapper;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class StandardReportRepository
{
    public async Task<ReportTable> ItemMarginAsync(DateOnly from, DateOnly to, Guid[] branchIds,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT i.code AS ItemCode, i.name_ar AS ItemName,
                   SUM(l.net_amount) AS Revenue,
                   SUM(l.quantity * COALESCE((
                       SELECT SUM(dnl.cost_total) / NULLIF(SUM(dnl.quantity), 0)
                       FROM delivery_note_lines dnl
                       WHERE dnl.booking_line_id = l.booking_line_id), 0)) AS Cogs,
                   SUM(l.quantity) AS Quantity
            FROM commercial_document_lines l
            JOIN commercial_documents d ON d.id = l.document_id
            JOIN items i ON i.id = l.item_id
            WHERE d.status = 'posted' AND d.doc_type = 'sales_invoice'
              AND d.doc_date BETWEEN @from AND @to
              AND (@allBranches OR d.branch_id = ANY(@branchIds))
            GROUP BY i.id, i.code, i.name_ar
            HAVING SUM(l.net_amount) <> 0
            ORDER BY SUM(l.net_amount) DESC
            LIMIT 300
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var rows = (await connection.QueryAsync<MarginRow>(new CommandDefinition(sql, new
        {
            from = from.ToDateTime(TimeOnly.MinValue), to = to.ToDateTime(TimeOnly.MinValue),
            branchIds, allBranches = branchIds.Length == 0
        }, cancellationToken: ct))).ToList();

        var columns = new List<ReportColumn>
        {
            new("code", "common.code"),
            new("name", "common.name_ar"),
            new("quantity", "report.quantity", ReportValueType.Number),
            new("revenue", "report.revenue", ReportValueType.Money),
            new("cogs", "report.cogs", ReportValueType.Money),
            new("margin", "report.margin", ReportValueType.Money),
            new("margin_pct", "report.margin_percent", ReportValueType.Number)
        };
        var data = rows.Select(r =>
        {
            var margin = r.Revenue - r.Cogs;
            var percent = r.Revenue == 0 ? 0 : margin / r.Revenue * 100;
            return (IReadOnlyList<string?>)
            [
                r.ItemCode, r.ItemName, Num(r.Quantity), Money(r.Revenue), Money(r.Cogs),
                Money(margin), Num(Math.Round(percent, 2))
            ];
        }).ToList();

        var totalMargin = rows.Sum(r => r.Revenue - r.Cogs);
        return new ReportTable(columns, data, $"report.margin_total={Money(totalMargin)}");
    }

    private static string Num(decimal value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string Money(decimal value) =>
        value.ToString("0.00", CultureInfo.InvariantCulture);

    private sealed class MarginRow
    {
        public string ItemCode { get; set; } = "";
        public string ItemName { get; set; } = "";
        public decimal Revenue { get; set; }
        public decimal Cogs { get; set; }
        public decimal Quantity { get; set; }
    }
}
