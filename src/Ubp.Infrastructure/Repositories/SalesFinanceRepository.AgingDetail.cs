using Dapper;
using Ubp.Core.Models;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class SalesFinanceRepository
{
    /// <summary>أعمار بالفاتورة: كل فاتورة مفتوحة بعمرها وسلتها حسب تاريخ الاستحقاق.</summary>
    public async Task<IReadOnlyList<AgingInvoiceRow>> CustomerInvoiceAgingAsync(Guid? branchId,
        DateTime asOf, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return (await connection.QueryAsync<AgingInvoiceRow>(new CommandDefinition("""
            SELECT d.id AS DocumentId, d.doc_no AS DocNo, c.name_ar AS PartyName,
                   d.doc_date AS DocDate, COALESCE(d.due_date, d.doc_date) AS DueDate,
                   (@asOf::date - COALESCE(d.due_date, d.doc_date)) AS AgeDays,
                   d.total - d.allocated_total AS Balance,
                   CASE WHEN @asOf::date - COALESCE(d.due_date, d.doc_date) <= 0 THEN 'current'
                        WHEN @asOf::date - COALESCE(d.due_date, d.doc_date) <= 30 THEN 'days30'
                        WHEN @asOf::date - COALESCE(d.due_date, d.doc_date) <= 60 THEN 'days60'
                        WHEN @asOf::date - COALESCE(d.due_date, d.doc_date) <= 90 THEN 'days90'
                        ELSE 'over90' END AS Bucket
            FROM commercial_documents d
            JOIN customers c ON c.id = d.customer_id
            WHERE d.doc_type = 'sales_invoice' AND d.status = 'posted'
              AND d.total > d.allocated_total
              AND (@branchId::uuid IS NULL OR d.branch_id = @branchId::uuid)
            ORDER BY AgeDays DESC, d.doc_date
            LIMIT 500
            """, new { branchId, asOf }, cancellationToken: ct))).ToList();
    }

    /// <summary>أعمار بالكمية: الكميات المحجوزة غير المنفذة لكل بند حجز وعمرها.</summary>
    public async Task<IReadOnlyList<AgingQuantityRow>> BookingQuantityAgingAsync(Guid? branchId,
        DateTime asOf, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return (await connection.QueryAsync<AgingQuantityRow>(new CommandDefinition("""
            SELECT b.id AS BookingId, b.doc_no AS BookingNo, c.name_ar AS CustomerName,
                   i.code AS ItemCode, i.name_ar AS ItemName,
                   (l.quantity - l.delivered_qty - l.cancelled_qty) AS RemainingQty,
                   (@asOf::date - b.booking_date) AS AgeDays,
                   CASE WHEN @asOf::date - b.booking_date <= 0 THEN 'current'
                        WHEN @asOf::date - b.booking_date <= 30 THEN 'days30'
                        WHEN @asOf::date - b.booking_date <= 60 THEN 'days60'
                        WHEN @asOf::date - b.booking_date <= 90 THEN 'days90'
                        ELSE 'over90' END AS Bucket
            FROM booking_lines l
            JOIN bookings b ON b.id = l.booking_id
            JOIN customers c ON c.id = b.customer_id
            JOIN items i ON i.id = l.item_id
            WHERE b.status IN ('confirmed', 'partially_delivered')
              AND (l.quantity - l.delivered_qty - l.cancelled_qty) > 0
              AND (@branchId::uuid IS NULL OR b.branch_id = @branchId::uuid)
            ORDER BY AgeDays DESC, b.doc_no, l.line_no
            LIMIT 500
            """, new { branchId, asOf }, cancellationToken: ct))).ToList();
    }
}
