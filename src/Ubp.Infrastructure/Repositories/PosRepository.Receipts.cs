using Dapper;
using Npgsql;
using Ubp.Core.Models;

namespace Ubp.Infrastructure.Repositories;

/// <summary>قراءة إيصالات القبض وعرضها (مفصولة عن دورة البيع للحفاظ على حدود الملف).</summary>
public sealed partial class PosRepository
{
    public async Task<IReadOnlyList<CommercialDocument>> ListReceiptsAsync(Guid? branchId,
        int limit, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return (await connection.QueryAsync<CommercialDocument>(new CommandDefinition("""
            SELECT v.id AS Id, v.voucher_type AS DocType, v.doc_no AS DocNo, v.doc_date AS DocDate,
                   v.status AS Status, v.branch_id AS BranchId, v.party_name AS PartyName,
                   v.total_amount AS Total, v.fee_total AS AllocatedTotal,
                   v.journal_entry_id AS JournalEntryId
            FROM vouchers v
            WHERE v.voucher_type = 'receipt' AND v.party_type = 'C'
              AND (EXISTS (SELECT 1 FROM bookings b
                           WHERE b.customer_id = v.party_id AND b.source = 'pos'
                             AND b.branch_id = v.branch_id AND b.booking_date = v.doc_date)
                   -- إيصالات تحصيل الخدمات المحجوزة (مرتبطة بالحجوزات عبر booking_payments)
                   OR EXISTS (SELECT 1 FROM booking_payments bp WHERE bp.voucher_id = v.id))
              AND (@branchId::uuid IS NULL OR v.branch_id = @branchId::uuid)
            ORDER BY v.doc_date DESC, v.id DESC LIMIT @limit
            """, new { branchId, limit = Math.Clamp(limit, 1, 200) }, cancellationToken: ct)))
            .ToList();
    }

    public async Task<(Guid? CustomerId, string? DocNo, decimal Total, Guid BranchId)>
        GetReceiptHeaderAsync(Guid voucherId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<(Guid? CustomerId, string? DocNo,
            decimal Total, Guid BranchId)>(new CommandDefinition("""
            SELECT party_id AS CustomerId, doc_no AS DocNo, total_amount AS Total,
                   branch_id AS BranchId
            FROM vouchers
            WHERE id = @id AND voucher_type = 'receipt' AND party_type = 'C'
            """, new { id = voucherId }, cancellationToken: ct));
    }

    public async Task<Voucher?> GetReceiptDetailAsync(Guid voucherId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var voucher = await connection.QuerySingleOrDefaultAsync<Voucher>(new CommandDefinition("""
            SELECT v.id AS Id, v.voucher_type AS VoucherType, v.branch_id AS BranchId,
                   v.doc_no AS DocNo, v.doc_date AS DocDate, v.status AS Status,
                   v.party_type AS PartyType, v.party_name AS PartyName, v.party_id AS PartyId,
                   v.treasury_id AS TreasuryId, t.code AS TreasuryCode,
                   t.name_ar AS TreasuryName, v.total_amount AS TotalAmount,
                   v.fee_total AS FeeTotal, v.notes AS Notes, v.posted_at AS PostedAt,
                   v.version AS Version
            FROM vouchers v JOIN treasuries t ON t.id = v.treasury_id
            WHERE v.id = @id AND v.voucher_type = 'receipt' AND v.party_type = 'C'
            """, new { id = voucherId }, cancellationToken: ct));
        if (voucher is null) return null;

        voucher.Lines = (await connection.QueryAsync<VoucherLine>(new CommandDefinition("""
            SELECT l.id AS Id, l.line_no AS LineNo, l.treasury_method_id AS TreasuryMethodId,
                   m.code AS MethodCode, m.name_ar AS MethodName, m.method_type AS MethodType,
                   l.amount AS Amount, l.fee_amount AS FeeAmount, l.reference AS Reference
            FROM voucher_lines l
            JOIN treasury_methods m ON m.id = l.treasury_method_id
            WHERE l.voucher_id = @id ORDER BY l.line_no
            """, new { id = voucherId }, cancellationToken: ct))).ToList();
        return voucher;
    }
}
