using Dapper;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class AssetRepository
{
    /// <summary>
    /// مزامنة قراءات عدّاد من أسطر الفواتير المرحّلة: كل سطر صنفه مرتبط بأصل معدود
    /// يُنشئ قراءة `SALES` مرة واحدة (فهرس فريد على source_line_id)، ثم تُحدَّث قيمة العدّاد.
    /// </summary>
    public async Task<int> SyncSalesReadingsAsync(Guid? branchId, DateOnly from, DateOnly to,
        Guid? userId, CancellationToken ct = default)
    {
        const string sql = """
            WITH candidates AS (
                SELECT i.metered_asset_id AS asset_id, l.id AS line_id, SUM(l.quantity) AS units
                FROM commercial_document_lines l
                JOIN commercial_documents d ON d.id = l.document_id
                JOIN items i ON i.id = l.item_id
                WHERE i.metered_asset_id IS NOT NULL
                  AND d.status = 'posted'
                  AND d.doc_type = 'sales_invoice'
                  AND d.doc_date >= @from AND d.doc_date <= @to
                  AND (@branchId IS NULL OR d.branch_id = @branchId)
                GROUP BY i.metered_asset_id, l.id
            ), inserted AS (
                INSERT INTO asset_meter_readings
                    (asset_id, value, delta, source, note, source_line_id, created_by)
                SELECT c.asset_id,
                       a.current_meter_value + c.units, c.units, 'SALES', 'قراءة من فاتورة بيع',
                       c.line_id, @userId
                FROM candidates c
                JOIN fixed_assets a ON a.id = c.asset_id
                WHERE a.status = 'ACTIVE' AND a.is_metered
                ON CONFLICT (asset_id, source_line_id) WHERE source_line_id IS NOT NULL DO NOTHING
                RETURNING asset_id, delta
            )
            UPDATE fixed_assets a
            SET current_meter_value = a.current_meter_value + i.total,
                updated_by = @userId, updated_at = now(), version = version + 1
            FROM (SELECT asset_id, SUM(delta) AS total FROM inserted GROUP BY asset_id) i
            WHERE a.id = i.asset_id
            RETURNING a.id
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<Guid>(new CommandDefinition(sql, new
        {
            branchId, from = from.ToDateTime(TimeOnly.MinValue),
            to = to.ToDateTime(TimeOnly.MinValue), userId
        }, cancellationToken: ct));
        return rows.Count();
    }
}
