using Dapper;
using Npgsql;
using Ubp.Core.Models;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class TreasuryRepository
{
    /// <summary>صف طريقة الدفع مع إعدادات الرسوم اللازمة لحساب المصاريف تلقائياً.</summary>
    private sealed class MethodRow
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = "";
        public string MethodType { get; set; } = PaymentMethodTypes.Cash;
        public Guid? ClearingAccountId { get; set; }
        public decimal FeePercent { get; set; }
        public decimal FeeFixed { get; set; }
        public bool RequiresReference { get; set; }
    }

    private sealed class TreasuryRow
    {
        public Guid Id { get; set; }
        public Guid BranchId { get; set; }
        public Guid AccountId { get; set; }
        public string TreasuryType { get; set; } = TreasuryTypes.Cash;
    }

    private sealed class VoucherLineRow
    {
        public Guid VoucherId { get; set; }
        public Guid Id { get; set; }
        public int LineNo { get; set; }
        public Guid TreasuryMethodId { get; set; }
        public string MethodCode { get; set; } = "";
        public string MethodName { get; set; } = "";
        public string MethodType { get; set; } = PaymentMethodTypes.Cash;
        public decimal Amount { get; set; }
        public decimal FeeAmount { get; set; }
        public string? Reference { get; set; }
    }

    private sealed class AllocationTarget
    {
        public Guid Id { get; set; }
        public string Status { get; set; } = "";
        public decimal Total { get; set; }
        public decimal AllocatedTotal { get; set; }
    }

    private const string VoucherHeadSelect = """
        SELECT v.id AS Id, v.voucher_type AS VoucherType, v.branch_id AS BranchId,
               v.doc_no AS DocNo, v.doc_date AS DocDate, v.status AS Status,
               v.party_type::text AS PartyType, v.party_id AS PartyId, v.party_name AS PartyName,
               v.treasury_id AS TreasuryId, t.code AS TreasuryCode, t.name_ar AS TreasuryName,
               v.to_treasury_id AS ToTreasuryId, tt.code AS ToTreasuryCode,
               tt.name_ar AS ToTreasuryName, v.total_amount AS TotalAmount,
               v.fee_total AS FeeTotal, v.journal_entry_id AS JournalEntryId,
               j.entry_no AS JournalNo, v.notes AS Notes, v.posted_at AS PostedAt,
               v.version AS Version
        FROM vouchers v
        JOIN treasuries t ON t.id = v.treasury_id
        LEFT JOIN treasuries tt ON tt.id = v.to_treasury_id
        LEFT JOIN journal_entries j ON j.id = v.journal_entry_id
        """;

    private static async Task<TreasuryRow?> GetTreasuryRowAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, Guid treasuryId, CancellationToken ct) =>
        await connection.QuerySingleOrDefaultAsync<TreasuryRow>(new CommandDefinition("""
            SELECT id AS Id, branch_id AS BranchId, account_id AS AccountId,
                   treasury_type AS TreasuryType
            FROM treasuries WHERE id = @treasuryId AND is_active
            """, new { treasuryId }, tx, cancellationToken: ct));

    private static async Task AttachLinesAsync(NpgsqlConnection connection,
        IReadOnlyList<Voucher> vouchers, CancellationToken ct)
    {
        if (vouchers.Count == 0) return;
        var ids = vouchers.Select(v => v.Id).ToArray();
        var rows = (await connection.QueryAsync<VoucherLineRow>(new CommandDefinition("""
            SELECT l.voucher_id AS VoucherId, l.id AS Id, l.line_no AS LineNo,
                   l.treasury_method_id AS TreasuryMethodId, m.code AS MethodCode,
                   m.name_ar AS MethodName, m.method_type AS MethodType, l.amount AS Amount,
                   l.fee_amount AS FeeAmount, l.reference AS Reference
            FROM voucher_lines l
            JOIN treasury_methods m ON m.id = l.treasury_method_id
            WHERE l.voucher_id = ANY(@ids)
            ORDER BY l.line_no
            """, new { ids }, cancellationToken: ct))).ToList();

        var byVoucher = rows.GroupBy(r => r.VoucherId).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var voucher in vouchers)
        {
            if (!byVoucher.TryGetValue(voucher.Id, out var lines)) continue;
            voucher.Lines = lines.Select(l => new VoucherLine
            {
                Id = l.Id, LineNo = l.LineNo, TreasuryMethodId = l.TreasuryMethodId,
                MethodCode = l.MethodCode, MethodName = l.MethodName, MethodType = l.MethodType,
                Amount = l.Amount, FeeAmount = l.FeeAmount, Reference = l.Reference
            }).ToList();
        }
    }
}
