using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IBookingCollectionRepository
{
    /// <summary>الخدمات المحجوزة للعميل في الفرع مع الإجمالي والمسدد والمستحق.</summary>
    Task<IReadOnlyList<BookingCollectionRow>> ListOutstandingAsync(Guid branchId, Guid customerId,
        CancellationToken ct = default);
    /// <summary>سداد مبالغ على حجوزات محددة بسند قبض مرحّل داخل معاملة واحدة.</summary>
    Task<BookingCollectionResult> CollectAsync(Guid voucherId, BookingCollectionInput input,
        Guid userId, CancellationToken ct = default);
}

/// <summary>
/// تحصيل قيمة الخدمات المحجوزة من نقطة البيع: سند قبض واحد لكل عملية، يُخصَّص على
/// الحجوزات المحددة في <c>booking_payments</c> (مصدر «المسدد سابقًا») ويُرحَّل على
/// حساب الخزينة/المقاصة مقابل دفعات العملاء المقدمة.
/// </summary>
public sealed partial class BookingCollectionRepository : IBookingCollectionRepository
{
    private sealed record EntryLine(Guid AccountId, decimal Debit, decimal Credit,
        Guid? CustomerId = null);

    private sealed class LockedBooking
    {
        public Guid Id { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal DepositAmount { get; set; }
        public decimal PaidAmount { get; set; }
    }

    private readonly IDbConnectionFactory _factory;
    public BookingCollectionRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<BookingCollectionRow>> ListOutstandingAsync(Guid branchId,
        Guid customerId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<BookingCollectionRow>(new CommandDefinition("""
            SELECT 'booking'::varchar AS Kind, v.booking_id AS BookingId,
                   v.booking_no AS BookingNo, v.booking_date AS BookingDate,
                   -- كل بنود الحجز (خدمات + تنفيذ مباشر) بكمياتها في عمود الخدمات — 0095.
                   COALESCE((SELECT string_agg(i.name_ar || ' × ' ||
                                trim(to_char(l.quantity, 'FM999999990.##')), ' • ')
                             FROM booking_lines l JOIN items i ON i.id = l.item_id
                             WHERE l.booking_id = v.booking_id),
                            v.items) AS Services,
                   v.total_amount AS TotalAmount, v.paid_amount AS PaidAmount,
                   v.remaining_amount AS RemainingAmount
            FROM v_customer_booking_balances v
            WHERE branch_id = @branchId AND customer_id = @customerId
              AND status NOT IN ('cancelled', 'reversed')
              AND remaining_amount > 0
            UNION ALL
            SELECT 'invoice', d.id, d.doc_no, d.doc_date,
                   COALESCE((SELECT string_agg(i.name_ar, ' • ')
                             FROM commercial_document_lines l
                             JOIN items i ON i.id = l.item_id
                             WHERE l.document_id = d.id), 'فاتورة/خدمة منفذة'),
                   d.total, d.allocated_total, d.total - d.allocated_total
            FROM commercial_documents d
            WHERE d.branch_id = @branchId AND d.customer_id = @customerId
              AND d.doc_type = 'sales_invoice' AND d.status = 'posted'
              AND d.total - d.allocated_total > 0
            ORDER BY 3, 2
            LIMIT 200
            """, new { branchId, customerId }, cancellationToken: ct));
        return rows.ToList();
    }
}
