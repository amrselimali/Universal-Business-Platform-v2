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
            SELECT booking_id AS BookingId, booking_no AS BookingNo, booking_date AS BookingDate,
                   services AS Services, total_amount AS TotalAmount, paid_amount AS PaidAmount,
                   remaining_amount AS RemainingAmount
            FROM v_customer_booking_balances
            WHERE branch_id = @branchId AND customer_id = @customerId
              AND status IN ('confirmed', 'partially_delivered', 'completed')
              AND services IS NOT NULL
              AND remaining_amount > 0
            ORDER BY booking_date, booking_no
            LIMIT 200
            """, new { branchId, customerId }, cancellationToken: ct));
        return rows.ToList();
    }
}
