using Dapper;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class StandardReportRepository
{
    /// <summary>استهلاك العدادات لكل عميل: قراءات SALES المرتبطة بسطر الفاتورة (مصدر واحد).</summary>
    public async Task<ReportTable> CustomerMeterUsageAsync(DateOnly from, DateOnly to,
        Guid[] branchIds, CancellationToken ct = default)
    {
        const string sql = """
            SELECT c.name_ar AS Customer, a.code AS AssetCode, a.name_ar AS AssetName,
                   COALESCE(a.meter_type, '-') AS MeterType,
                   SUM(r.delta) AS Units, COUNT(*)::int AS Readings,
                   MAX(r.reading_at) AS LastReading
            FROM asset_meter_readings r
            JOIN fixed_assets a ON a.id = r.asset_id
            JOIN commercial_document_lines l ON l.id = r.source_line_id
            JOIN commercial_documents d ON d.id = l.document_id
            JOIN customers c ON c.id = d.customer_id
            WHERE r.source = 'SALES' AND d.status = 'posted'
              AND d.doc_date BETWEEN @from AND @to
              AND (@allBranches OR d.branch_id = ANY(@branchIds))
            GROUP BY c.id, c.name_ar, a.code, a.name_ar, a.meter_type
            ORDER BY SUM(r.delta) DESC
            LIMIT 300
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var rows = (await connection.QueryAsync<MeterUsageRow>(new CommandDefinition(sql, new
        {
            from = from.ToDateTime(TimeOnly.MinValue), to = to.ToDateTime(TimeOnly.MinValue),
            branchIds, allBranches = branchIds.Length == 0
        }, cancellationToken: ct))).ToList();

        var columns = new List<ReportColumn>
        {
            new("customer", "report.customer"),
            new("asset", "report.asset"),
            new("meter_type", "assets.meter_type"),
            new("units", "report.units", ReportValueType.Number),
            new("readings", "report.readings", ReportValueType.Number),
            new("last_reading", "report.last_reading", ReportValueType.Date)
        };
        var data = rows.Select(r => (IReadOnlyList<string?>)
        [
            r.Customer, $"{r.AssetCode} — {r.AssetName}", r.MeterType, Num(r.Units),
            r.Readings.ToString(), r.LastReading.ToLocalTime().ToString("yyyy-MM-dd")
        ]).ToList();

        return new ReportTable(columns, data, $"report.units_total={Num(rows.Sum(r => r.Units))}");
    }

    private sealed class MeterUsageRow
    {
        public string Customer { get; set; } = "";
        public string AssetCode { get; set; } = "";
        public string AssetName { get; set; } = "";
        public string MeterType { get; set; } = "";
        public decimal Units { get; set; }
        public int Readings { get; set; }
        public DateTimeOffset LastReading { get; set; }
    }
}
