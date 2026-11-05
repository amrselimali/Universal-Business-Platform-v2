using Dapper;
using Npgsql;
using Ubp.Core.Models;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class TreasuryRepository
{
    /// <summary>حصر تسجيلات الوردية (غير الملغاة) لتُراجَع: سندات ومستندات وحجوزات ومصروفات مسودة.</summary>
    private static async Task CaptureReviewItemsAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, ShiftRow shift, CancellationToken ct) =>
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO shift_review_items (id, shift_id, screen_code, entity_type, entity_id,
                doc_no, doc_date, party_name, amount, doc_status)
            SELECT gen_random_uuid(), @shiftId, x.screen_code, x.entity_type, x.entity_id,
                   x.doc_no, x.doc_date, x.party_name, x.amount, x.doc_status
            FROM (
                SELECT 'TREASURY_VOUCHERS'::varchar AS screen_code, 'voucher'::varchar AS entity_type,
                       v.id AS entity_id, v.doc_no, v.doc_date, v.party_name,
                       v.total_amount AS amount, v.status AS doc_status
                FROM vouchers v
                WHERE v.branch_id = @branchId AND v.status = 'draft'
                  AND v.created_at >= @openedAt
                UNION ALL
                SELECT CASE d.doc_type
                           WHEN 'purchase_invoice' THEN 'PURCHASE_INVOICES'
                           WHEN 'purchase_return' THEN 'PURCHASE_RETURNS'
                           WHEN 'sales_return' THEN 'SALES_RETURNS'
                           ELSE 'SALES_INVOICES' END,
                       'commercial_document', d.id, d.doc_no, d.doc_date, d.party_name,
                       d.total, d.status
                FROM commercial_documents d
                WHERE d.branch_id = @branchId AND d.status = 'draft'
                  AND d.created_at >= @openedAt
                UNION ALL
                SELECT 'SALES_BOOKINGS', 'booking', b.id, b.doc_no, b.booking_date,
                       c.name_ar, b.total_amount, b.status
                FROM bookings b
                LEFT JOIN customers c ON c.id = b.customer_id
                WHERE b.branch_id = @branchId AND b.status = 'draft'
                  AND b.created_at >= @openedAt
                UNION ALL
                SELECT 'OPERATING_EXPENSES', 'operating_expense', o.id, o.doc_no, o.doc_date,
                       s.name_ar, o.total_amount, lower(o.status)
                FROM operating_expenses o
                JOIN suppliers s ON s.id = o.supplier_id
                WHERE o.branch_id = @branchId AND o.status = 'DRAFT'
                  AND o.created_at >= @openedAt
            ) x
            ON CONFLICT (shift_id, entity_type, entity_id) DO NOTHING
            """, new { shiftId = shift.Id, branchId = shift.BranchId, openedAt = shift.OpenedAt },
            tx, cancellationToken: ct));
}
