using Dapper;
using Ubp.Core.Models;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class SalesFinanceRepository
{
    /// <summary>أعمار فواتير العملاء: الرصيد المفتوح موزّعاً على 5 سلال حسب تاريخ الاستحقاق.</summary>
    public async Task<IReadOnlyList<AgingBucketRow>> CustomerAgingAsync(Guid? branchId,
        DateTime asOf, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return (await connection.QueryAsync<AgingBucketRow>(new CommandDefinition("""
            SELECT c.id AS PartyId, c.name_ar AS PartyName,
                   SUM(CASE WHEN @asOf::date - COALESCE(d.due_date, d.doc_date) <= 0
                            THEN d.total - d.allocated_total ELSE 0 END) AS Current,
                   SUM(CASE WHEN @asOf::date - COALESCE(d.due_date, d.doc_date) BETWEEN 1 AND 30
                            THEN d.total - d.allocated_total ELSE 0 END) AS Days30,
                   SUM(CASE WHEN @asOf::date - COALESCE(d.due_date, d.doc_date) BETWEEN 31 AND 60
                            THEN d.total - d.allocated_total ELSE 0 END) AS Days60,
                   SUM(CASE WHEN @asOf::date - COALESCE(d.due_date, d.doc_date) BETWEEN 61 AND 90
                            THEN d.total - d.allocated_total ELSE 0 END) AS Days90,
                   SUM(CASE WHEN @asOf::date - COALESCE(d.due_date, d.doc_date) > 90
                            THEN d.total - d.allocated_total ELSE 0 END) AS Over90
            FROM commercial_documents d
            JOIN customers c ON c.id = d.customer_id
            WHERE d.doc_type = 'sales_invoice' AND d.status = 'posted'
              AND d.total > d.allocated_total
              AND (@branchId::uuid IS NULL OR d.branch_id = @branchId::uuid)
            GROUP BY c.id, c.name_ar
            HAVING SUM(d.total - d.allocated_total) <> 0
            ORDER BY c.name_ar
            """, new { branchId, asOf }, cancellationToken: ct))).ToList();
    }

    /// <summary>أعمار الدفعات المقدمة: أرصدة سندات القبض غير المخصصة حسب عمر السند.</summary>
    public async Task<IReadOnlyList<AgingBucketRow>> AdvancesAgingAsync(Guid? branchId,
        DateTime asOf, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return (await connection.QueryAsync<AgingBucketRow>(new CommandDefinition("""
            SELECT x.party_id AS PartyId, c.name_ar AS PartyName,
                   SUM(CASE WHEN @asOf::date - x.doc_date <= 0 THEN x.remaining ELSE 0 END) AS Current,
                   SUM(CASE WHEN @asOf::date - x.doc_date BETWEEN 1 AND 30 THEN x.remaining ELSE 0 END) AS Days30,
                   SUM(CASE WHEN @asOf::date - x.doc_date BETWEEN 31 AND 60 THEN x.remaining ELSE 0 END) AS Days60,
                   SUM(CASE WHEN @asOf::date - x.doc_date BETWEEN 61 AND 90 THEN x.remaining ELSE 0 END) AS Days90,
                   SUM(CASE WHEN @asOf::date - x.doc_date > 90 THEN x.remaining ELSE 0 END) AS Over90
            FROM (
                SELECT v.id, v.party_id, v.doc_date,
                       v.total_amount - COALESCE(SUM(a.amount), 0) AS remaining
                FROM vouchers v
                LEFT JOIN document_allocations a ON a.source_voucher_id = v.id
                WHERE v.status = 'posted' AND v.voucher_type = 'receipt'
                  AND v.party_type = 'C'
                  AND (@branchId::uuid IS NULL OR v.branch_id = @branchId::uuid)
                GROUP BY v.id, v.party_id, v.doc_date, v.total_amount
                HAVING v.total_amount - COALESCE(SUM(a.amount), 0) > 0
            ) x
            JOIN customers c ON c.id = x.party_id
            GROUP BY x.party_id, c.name_ar
            ORDER BY c.name_ar
            """, new { branchId, asOf }, cancellationToken: ct))).ToList();
    }

    /// <summary>أعمار الحجوزات المفتوحة: قيمة الحجز ناقص ما فُوتر منه، حسب عمر الحجز.</summary>
    public async Task<IReadOnlyList<AgingBucketRow>> BookingAgingAsync(Guid? branchId,
        DateTime asOf, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return (await connection.QueryAsync<AgingBucketRow>(new CommandDefinition("""
            SELECT b.id AS PartyId, COALESCE(b.doc_no, '(draft)') AS PartyName,
                   c.name_ar AS Reference,
                   SUM(CASE WHEN @asOf::date - b.booking_date <= 0 THEN open ELSE 0 END) AS Current,
                   SUM(CASE WHEN @asOf::date - b.booking_date BETWEEN 1 AND 30 THEN open ELSE 0 END) AS Days30,
                   SUM(CASE WHEN @asOf::date - b.booking_date BETWEEN 31 AND 60 THEN open ELSE 0 END) AS Days60,
                   SUM(CASE WHEN @asOf::date - b.booking_date BETWEEN 61 AND 90 THEN open ELSE 0 END) AS Days90,
                   SUM(CASE WHEN @asOf::date - b.booking_date > 90 THEN open ELSE 0 END) AS Over90
            FROM bookings b
            JOIN customers c ON c.id = b.customer_id
            CROSS JOIN LATERAL (
                SELECT GREATEST(b.total_amount - COALESCE((
                    SELECT SUM(d.total) FROM commercial_documents d
                    WHERE d.booking_id = b.id AND d.doc_type = 'sales_invoice'
                      AND d.status = 'posted'), 0), 0) AS open
            ) o
            WHERE b.status IN ('confirmed', 'partially_delivered')
              AND (@branchId::uuid IS NULL OR b.branch_id = @branchId::uuid)
            GROUP BY b.id, b.doc_no, b.booking_date, c.name_ar
            HAVING GREATEST(b.total_amount - COALESCE((
                SELECT SUM(d.total) FROM commercial_documents d
                WHERE d.booking_id = b.id AND d.doc_type = 'sales_invoice'
                  AND d.status = 'posted'), 0), 0) > 0
            ORDER BY b.booking_date
            """, new { branchId, asOf }, cancellationToken: ct))).ToList();
    }

    public async Task<IReadOnlyList<UnpostedDocument>> ListUnpostedAsync(Guid? branchId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return (await connection.QueryAsync<UnpostedDocument>(new CommandDefinition("""
            SELECT kind AS Kind, id AS Id, branch_id AS BranchId, doc_type AS DocType,
                   doc_no AS DocNo, doc_date AS DocDate, status AS Status, amount AS Amount
            FROM v_unposted_documents
            WHERE (@branchId::uuid IS NULL OR branch_id = @branchId::uuid)
            ORDER BY doc_date DESC
            LIMIT 200
            """, new { branchId }, cancellationToken: ct))).ToList();
    }

    /// <summary>ثلاث مطابقات (D19): مخزون، عملاء، موردون — كلها بالرصيد الصافي.</summary>
    public async Task<IReadOnlyList<ReconciliationCheck>> ReconcileAsync(Guid? branchId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var stockDoc = await connection.ExecuteScalarAsync<decimal>(new CommandDefinition(
            "SELECT COALESCE(SUM(inventory_value), 0) FROM stock_balances", cancellationToken: ct));
        var stockGl = await connection.ExecuteScalarAsync<decimal>(new CommandDefinition("""
            SELECT COALESCE(SUM(g.debit - g.credit), 0) FROM gl_balances g
            JOIN posting_defaults p ON p.account_id = g.account_id AND p.setting_key = 'stock_inventory'
            WHERE (@branchId::uuid IS NULL OR g.branch_id = @branchId::uuid)
            """, new { branchId }, cancellationToken: ct));

        var customersDoc = await connection.ExecuteScalarAsync<decimal>(new CommandDefinition("""
            SELECT COALESCE((SELECT SUM(d.total - d.allocated_total) FROM commercial_documents d
                             WHERE d.doc_type = 'sales_invoice' AND d.status = 'posted'), 0)
                 - COALESCE((SELECT SUM(v.total_amount - COALESCE(a.allocated, 0))
                             FROM vouchers v
                             LEFT JOIN (SELECT source_voucher_id, SUM(amount) AS allocated
                                        FROM document_allocations GROUP BY source_voucher_id) a
                                    ON a.source_voucher_id = v.id
                             WHERE v.status = 'posted' AND v.voucher_type = 'receipt'
                               AND v.party_type = 'C'), 0)
            """, cancellationToken: ct));
        var customersGl = await connection.ExecuteScalarAsync<decimal>(new CommandDefinition("""
            SELECT COALESCE(SUM(debit - credit), 0) FROM party_balances
            WHERE party_type = 'C' AND (@branchId::uuid IS NULL OR branch_id = @branchId::uuid)
            """, new { branchId }, cancellationToken: ct));

        var suppliersDoc = await connection.ExecuteScalarAsync<decimal>(new CommandDefinition("""
            SELECT COALESCE((SELECT SUM(d.total - d.allocated_total) FROM commercial_documents d
                             WHERE d.doc_type = 'purchase_invoice' AND d.status = 'posted'), 0)
                 - COALESCE((SELECT SUM(v.total_amount - COALESCE(a.allocated, 0))
                             FROM vouchers v
                             LEFT JOIN (SELECT source_voucher_id, SUM(amount) AS allocated
                                        FROM document_allocations GROUP BY source_voucher_id) a
                                    ON a.source_voucher_id = v.id
                             WHERE v.status = 'posted' AND v.voucher_type = 'payment'
                               AND v.party_type = 'S'), 0)
            """, cancellationToken: ct));
        var suppliersGl = await connection.ExecuteScalarAsync<decimal>(new CommandDefinition("""
            SELECT COALESCE(SUM(debit - credit), 0) FROM party_balances
            WHERE party_type = 'S' AND (@branchId::uuid IS NULL OR branch_id = @branchId::uuid)
            """, new { branchId }, cancellationToken: ct));

        return new List<ReconciliationCheck>
        {
            new() { Key = "stock", DocumentValue = stockDoc, GlValue = stockGl },
            new() { Key = "customers", DocumentValue = customersDoc, GlValue = customersGl },
            new() { Key = "suppliers", DocumentValue = -suppliersDoc, GlValue = -suppliersGl }
        };
    }
}
