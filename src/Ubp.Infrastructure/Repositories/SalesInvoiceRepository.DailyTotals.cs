using Dapper;
using Npgsql;

namespace Ubp.Infrastructure.Repositories;

/// <summary>
/// تحديث إجماليات المبيعات اليومية داخل **نفس** معاملة ترحيل المستند،
/// فيظل جدول اللوحة متسقاً مع الفواتير حتى لو تراجع الترحيل.
/// </summary>
public sealed partial class SalesInvoiceRepository
{
    private static async Task UpsertDailyTotalsAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, PostHead head, int sign, CancellationToken ct)
    {
        const string sql = """
            WITH costs AS (
                SELECT COALESCE(SUM(l.quantity * COALESCE(
                    (SELECT SUM(dnl.cost_total) / NULLIF(SUM(dnl.quantity), 0)
                     FROM delivery_note_lines dnl
                     WHERE dnl.booking_line_id = l.booking_line_id), 0)), 0)::numeric(18,4) AS cogs
                FROM commercial_document_lines l
                WHERE l.document_id = @id
            )
            INSERT INTO sales_daily_totals
                (day, branch_id, invoice_count, return_count, net_total, tax_total,
                 gross_total, cogs_total)
            SELECT @day::date, @branchId,
                   CASE WHEN @sign > 0 THEN 1 ELSE 0 END,
                   CASE WHEN @sign < 0 THEN 1 ELSE 0 END,
                   @sign * @net, @sign * @tax, @sign * @gross, @sign * costs.cogs
            FROM costs
            ON CONFLICT (day, branch_id) DO UPDATE SET
                invoice_count = sales_daily_totals.invoice_count + EXCLUDED.invoice_count,
                return_count  = sales_daily_totals.return_count + EXCLUDED.return_count,
                net_total     = sales_daily_totals.net_total + EXCLUDED.net_total,
                tax_total     = sales_daily_totals.tax_total + EXCLUDED.tax_total,
                gross_total   = sales_daily_totals.gross_total + EXCLUDED.gross_total,
                cogs_total    = sales_daily_totals.cogs_total + EXCLUDED.cogs_total,
                updated_at    = now()
            """;
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            id = head.Id,
            day = head.DocDate,
            branchId = head.BranchId,
            sign,
            net = head.Subtotal,
            tax = head.TaxTotal,
            gross = head.Total
        }, tx, cancellationToken: ct));
    }
}
