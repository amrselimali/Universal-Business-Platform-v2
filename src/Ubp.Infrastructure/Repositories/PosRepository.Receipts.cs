using Dapper;
using Npgsql;
using Ubp.Core.Models;

namespace Ubp.Infrastructure.Repositories;

/// <summary>قراءة إيصالات القبض وعرضها (مفصولة عن دورة البيع للحفاظ على حدود الملف).</summary>
public sealed partial class PosRepository
{
    private sealed class ReceiptDbLine
    {
        public Guid BookingId { get; set; }
        public string? BookingNo { get; set; }
        public bool IsSale { get; set; }
        public string? ItemName { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal NetAmount { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal BookingTotal { get; set; }
        public decimal BookingRemaining { get; set; }
        public decimal PaidBefore { get; set; }
    }

    public async Task<IReadOnlyList<CommercialDocument>> ListReceiptsAsync(Guid? branchId,
        DateTime? from, DateTime? to, int limit, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return (await connection.QueryAsync<CommercialDocument>(new CommandDefinition("""
            SELECT v.id AS Id, v.voucher_type AS DocType, v.doc_no AS DocNo, v.doc_date AS DocDate,
                   v.status AS Status, v.branch_id AS BranchId, v.party_name AS PartyName,
                   v.total_amount AS Total, v.fee_total AS AllocatedTotal,
                   v.journal_entry_id AS JournalEntryId,
                   t.name_ar AS TreasuryName,
                   -- مضمون جدول الإيصال في خلية واحدة: خدمات البيع (كمية/قيمة) أو حجوزات السداد — 0095.
                   COALESCE(
                     (SELECT string_agg(x.txt, ' • ')
                      FROM (
                        SELECT i.name_ar || ' × ' ||
                               trim(to_char(bl.quantity, 'FM999999990.##')) || ' — ' ||
                               trim(to_char(bl.net_amount, 'FM999999990.00')) AS txt
                        FROM booking_payments bp
                        JOIN bookings b ON b.id = bp.booking_id AND b.created_at >= v.created_at
                        JOIN booking_lines bl ON bl.booking_id = b.id
                        JOIN items i ON i.id = bl.item_id
                        WHERE bp.voucher_id = v.id
                        ORDER BY bl.line_no
                      ) x),
                     (SELECT string_agg(y.txt, ' • ')
                      FROM (
                        SELECT b.doc_no || ' — ' ||
                               (SELECT string_agg(DISTINCT i2.name_ar, '، ')
                                FROM booking_lines bl2 JOIN items i2 ON i2.id = bl2.item_id
                                WHERE bl2.booking_id = b.id) || ' — ' ||
                               (SELECT string_agg(DISTINCT trim(to_char(bl3.unit_price, 'FM999999990.00')), '، ')
                                FROM booking_lines bl3 WHERE bl3.booking_id = b.id) AS txt
                        FROM booking_payments bp
                        JOIN bookings b ON b.id = bp.booking_id
                        WHERE bp.voucher_id = v.id
                        ORDER BY b.doc_no
                      ) y)) AS Description
            FROM vouchers v
            LEFT JOIN treasuries t ON t.id = v.treasury_id
            WHERE v.voucher_type = 'receipt' AND v.party_type = 'C'
              AND (EXISTS (SELECT 1 FROM bookings b
                           WHERE b.customer_id = v.party_id AND b.source = 'pos'
                             AND b.branch_id = v.branch_id AND b.booking_date = v.doc_date)
                   OR EXISTS (SELECT 1 FROM booking_payments bp WHERE bp.voucher_id = v.id))
              AND (@branchId::uuid IS NULL OR v.branch_id = @branchId::uuid)
              AND (@from::date IS NULL OR v.doc_date >= @from::date)
              AND (@to::date IS NULL OR v.doc_date <= @to::date)
            ORDER BY v.doc_date DESC, v.id DESC LIMIT @limit
            """, new { branchId, from, to, limit = Math.Clamp(limit, 1, 500) },
            cancellationToken: ct))).ToList();
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

    /// <summary>تفاصيل إيصال قبض: سطور الدفع + جدول الإيصال (خدمات/حجوزات) والإجماليات — 0095.</summary>
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

        var head = await connection.QuerySingleAsync<(DateTime CreatedAt, string? DocNo)>(
            new CommandDefinition(
                "SELECT created_at AS CreatedAt, doc_no AS DocNo FROM vouchers WHERE id = @id",
                new { id = voucherId }, cancellationToken: ct));
        var createdAt = head.CreatedAt;
        // ترتيب الإيصالات داخل نفس عملية التحصيل: الأقدم رقمًا هو الإيصال الأول — 0095.
        var docNo = head.DocNo ?? "";

        var rows = (await connection.QueryAsync<ReceiptDbLine>(new CommandDefinition("""
            SELECT b.id AS BookingId, b.doc_no AS BookingNo, i.name_ar AS ItemName,
                   bl.quantity AS Quantity, bl.unit_price AS UnitPrice,
                   bl.net_amount AS NetAmount, bl.tax_amount AS TaxAmount,
                   b.total_amount AS BookingTotal,
                   (b.created_at >= @createdAt) AS IsSale,
                   -- المتبقي بعد هذا الإيصال: يخصم دفعات هذا الإيصال والإيصالات الأسبق في نفس العملية.
                   GREATEST(b.total_amount - b.deposit_amount - COALESCE((
                       SELECT SUM(bp2.amount)
                       FROM booking_payments bp2
                       JOIN vouchers w2 ON w2.id = bp2.voucher_id
                       WHERE bp2.booking_id = b.id
                         AND (w2.created_at < @createdAt
                              OR (w2.created_at = @createdAt
                                  AND COALESCE(w2.doc_no, '') <= @docNo))), 0), 0)
                     AS BookingRemaining,
                   -- المسدد سابقًا: دفعات الإيصالات الأسبق فقط (ترتيب إنشاء الإيصال/رقمه).
                   b.deposit_amount + COALESCE((
                       SELECT SUM(bp3.amount)
                       FROM booking_payments bp3
                       JOIN vouchers w3 ON w3.id = bp3.voucher_id
                       WHERE bp3.booking_id = b.id
                         AND (w3.created_at < @createdAt
                              OR (w3.created_at = @createdAt
                                  AND COALESCE(w3.doc_no, '') < @docNo))), 0)
                     AS PaidBefore
            FROM booking_payments bp
            JOIN bookings b ON b.id = bp.booking_id
            JOIN booking_lines bl ON bl.booking_id = b.id
            JOIN items i ON i.id = bl.item_id
            WHERE bp.voucher_id = @id
            ORDER BY b.doc_no, bl.line_no
            """, new { id = voucherId, createdAt, docNo }, cancellationToken: ct))).ToList();

        var sale = rows.Any(r => r.IsSale);
        voucher.IsCollectionReceipt = rows.Count > 0 && !sale;
        if (sale)
        {
            var saleRows = rows.Where(r => r.IsSale).ToList();
            voucher.DisplayLines = saleRows.Select(r => new ReceiptLineDisplay
            {
                Description = r.ItemName ?? "", ServiceName = r.ItemName,
                Quantity = r.Quantity, UnitPrice = r.UnitPrice, Value = r.NetAmount
            }).ToList();
            voucher.DueTotal = Math.Round(saleRows.Sum(r => r.NetAmount), 2,
                MidpointRounding.AwayFromZero);
            voucher.TaxTotal = Math.Round(saleRows.Sum(r => r.TaxAmount), 2,
                MidpointRounding.AwayFromZero);
            // المتبقي بعد هذا الإيصال = متبقي الحجوزات بعد خصم دفعات هذا الإيصال والأسبق منها.
            voucher.RemainingDue = Math.Round(rows.GroupBy(r => r.BookingId)
                .Sum(g => g.First().BookingRemaining), 2, MidpointRounding.AwayFromZero);
            voucher.PaidBefore = Math.Round(rows.GroupBy(r => r.BookingId)
                .Sum(g => g.First().PaidBefore), 2, MidpointRounding.AwayFromZero);
        }
        else
        {
            // سداد خدمات محجوزة: كل حجز صف واحد (رقم الحجز — الخدمة — سعر الوحدة) بقيمته الأصلية.
            voucher.DisplayLines = rows.GroupBy(r => r.BookingId).Select(g =>
            {
                var services = string.Join("، ", g.Select(x => x.ItemName).Distinct());
                var prices = string.Join("، ", g.Select(x => x.UnitPrice.ToString("N2"))
                    .Distinct());
                return new ReceiptLineDisplay
                {
                    Description = $"{g.First().BookingNo} — {services} — {prices}",
                    ServiceName = services, Quantity = g.Sum(x => x.Quantity),
                    UnitPrice = g.First().UnitPrice, Value = g.First().BookingTotal
                };
            }).ToList();
            voucher.DueTotal = Math.Round(voucher.DisplayLines.Sum(l => l.Value), 2,
                MidpointRounding.AwayFromZero);
            voucher.TaxTotal = 0m;
            voucher.RemainingDue = Math.Round(rows.GroupBy(r => r.BookingId)
                .Sum(g => g.First().BookingRemaining), 2, MidpointRounding.AwayFromZero);
            voucher.PaidBefore = Math.Round(rows.GroupBy(r => r.BookingId)
                .Sum(g => g.First().PaidBefore), 2, MidpointRounding.AwayFromZero);
        }

        return voucher;
    }
}
