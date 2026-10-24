using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface ISalesFinanceRepository
{
    Task<Guid> CreateSettlementAsync(Guid id, SettlementInput input, Guid userId,
        CancellationToken ct = default);
    Task<Settlement?> GetSettlementAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Settlement>> ListSettlementsAsync(Guid? branchId, string? status, int limit,
        CancellationToken ct = default);
    Task<Settlement> ApproveSettlementAsync(Guid id, Guid userId, CancellationToken ct = default);
    Task<Settlement> PostSettlementAsync(Guid id, Guid userId, CancellationToken ct = default);

    Task<int> CalculateCommissionsAsync(CommissionInput input, Guid userId,
        CancellationToken ct = default);
    Task<IReadOnlyList<CommissionRow>> ListCommissionsAsync(DateTime month, Guid? employeeId,
        CancellationToken ct = default);

    Task<IReadOnlyList<AgingBucketRow>> CustomerAgingAsync(Guid? branchId, DateTime asOf,
        CancellationToken ct = default);
    Task<IReadOnlyList<AgingBucketRow>> AdvancesAgingAsync(Guid? branchId, DateTime asOf,
        CancellationToken ct = default);
    Task<IReadOnlyList<AgingBucketRow>> BookingAgingAsync(Guid? branchId, DateTime asOf,
        CancellationToken ct = default);
    Task<IReadOnlyList<AgingInvoiceRow>> CustomerInvoiceAgingAsync(Guid? branchId, DateTime asOf,
        CancellationToken ct = default);
    Task<IReadOnlyList<AgingQuantityRow>> BookingQuantityAgingAsync(Guid? branchId, DateTime asOf,
        CancellationToken ct = default);

    Task<IReadOnlyList<UnpostedDocument>> ListUnpostedAsync(Guid? branchId,
        CancellationToken ct = default);
    Task<IReadOnlyList<ReconciliationCheck>> ReconcileAsync(Guid? branchId,
        CancellationToken ct = default);
}

/// <summary>تسويات الحجوزات (D17) بعشرة أنواع دورة: استرداد/إلغاء/تحويل/مصادرة.</summary>
public sealed partial class SalesFinanceRepository : ISalesFinanceRepository
{
    private readonly IDbConnectionFactory _factory;
    public SalesFinanceRepository(IDbConnectionFactory factory) => _factory = factory;

    private sealed class SettlementRow
    {
        public Guid Id { get; set; }
        public Guid BranchId { get; set; }
        public string? DocNo { get; set; }
        public DateTime DocDate { get; set; }
        public string Status { get; set; } = CommercialStatuses.Draft;
        public Guid BookingId { get; set; }
        public string? BookingNo { get; set; }
        public string? CustomerName { get; set; }
        public string SettlementType { get; set; } = SettlementTypes.Refund;
        public decimal Amount { get; set; }
        public Guid? TreasuryId { get; set; }
        public string? TreasuryName { get; set; }
        public Guid? TargetBookingId { get; set; }
        public string? Reason { get; set; }
        public Guid? ApprovedBy { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public Guid? JournalEntryId { get; set; }
        public string? Notes { get; set; }
        public int Version { get; set; }
    }

    private sealed record EntryLine(Guid AccountId, decimal Debit, decimal Credit,
        Guid? CustomerId = null);

    private const string Select = """
        SELECT s.id AS Id, s.branch_id AS BranchId, s.doc_no AS DocNo, s.doc_date AS DocDate,
               s.status AS Status, s.booking_id AS BookingId, b.doc_no AS BookingNo,
               c.name_ar AS CustomerName, s.settlement_type AS SettlementType,
               s.amount AS Amount, s.treasury_id AS TreasuryId, t.name_ar AS TreasuryName,
               s.target_booking_id AS TargetBookingId, s.reason AS Reason,
               s.approved_by AS ApprovedBy, s.approved_at AS ApprovedAt,
               s.journal_entry_id AS JournalEntryId, s.notes AS Notes, s.version AS Version
        FROM booking_settlements s
        JOIN bookings b ON b.id = s.booking_id
        JOIN customers c ON c.id = b.customer_id
        LEFT JOIN treasuries t ON t.id = s.treasury_id
        """;

    public async Task<Guid> CreateSettlementAsync(Guid id, SettlementInput input, Guid userId,
        CancellationToken ct = default)
    {
        if (!SettlementTypes.IsValid(input.SettlementType))
            throw new ValidationAppException("validation.settlement_type");
        if (input.Amount < 0)
            throw new ValidationAppException("validation.settlement_amount");
        if (SettlementTypes.HasRefund(input.SettlementType) && input.TreasuryId is null)
            throw new ValidationAppException("validation.settlement_treasury_required");
        if (input.SettlementType == SettlementTypes.Transfer && input.TargetBookingId is null)
            throw new ValidationAppException("validation.settlement_target_required");

        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var booking = await connection.QuerySingleOrDefaultAsync<(Guid Id, Guid BranchId, Guid CustomerId, string Status)>(
            new CommandDefinition(
                "SELECT id, branch_id, customer_id, status FROM bookings WHERE id = @id",
                new { id = input.BookingId }, tx, cancellationToken: ct));
        if (booking.Id == Guid.Empty) throw new NotFoundAppException();
        if (booking.BranchId != input.BranchId)
            throw new ValidationAppException("validation.booking_branch_mismatch");

        if (input.TargetBookingId is not null)
        {
            var target = await connection.QuerySingleOrDefaultAsync<(Guid CustomerId, string Status)>(
                new CommandDefinition(
                    "SELECT customer_id, status FROM bookings WHERE id = @id",
                    new { id = input.TargetBookingId }, tx, cancellationToken: ct));
            if (target.CustomerId == Guid.Empty)
                throw new ValidationAppException("validation.settlement_target_not_found");
            if (target.CustomerId != booking.CustomerId)
                throw new ValidationAppException("validation.settlement_target_customer");
        }

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO booking_settlements (id, branch_id, doc_date, status, booking_id,
                settlement_type, amount, treasury_id, target_booking_id, reason, notes, created_by)
            VALUES (@id, @BranchId, @docDate, 'draft', @BookingId, @SettlementType, @Amount,
                    @TreasuryId, @TargetBookingId, @Reason, @Notes, @userId)
            """, new { id, input.BranchId, docDate = input.DocDate.Date, input.BookingId,
                input.SettlementType, input.Amount, input.TreasuryId, input.TargetBookingId,
                input.Reason, input.Notes, userId }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return id;
    }

    public async Task<Settlement?> GetSettlementAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<Settlement>(new CommandDefinition(
            Select + " WHERE s.id = @id", new { id }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<Settlement>> ListSettlementsAsync(Guid? branchId,
        string? status, int limit, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return (await connection.QueryAsync<Settlement>(new CommandDefinition(Select + """
             WHERE (@branchId::uuid IS NULL OR s.branch_id = @branchId::uuid)
               AND (@status::varchar IS NULL OR s.status = @status::varchar)
             ORDER BY s.doc_date DESC, s.id DESC LIMIT @limit
            """, new { branchId, status, limit = Math.Clamp(limit, 1, 500) },
            cancellationToken: ct))).ToList();
    }

    public async Task<Settlement> ApproveSettlementAsync(Guid id, Guid userId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var updated = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE booking_settlements SET approved_by = @userId, approved_at = now(),
                updated_by = @userId
            WHERE id = @id AND status = 'draft'
            """, new { userId, id }, cancellationToken: ct));
        if (updated == 0) throw new ConflictException();

        return (await GetSettlementAsync(id, ct))!;
    }

    public async Task<Settlement> PostSettlementAsync(Guid id, Guid userId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var row = await connection.QuerySingleOrDefaultAsync<SettlementRow>(new CommandDefinition("""
            SELECT id AS Id, branch_id AS BranchId, doc_no AS DocNo, doc_date AS DocDate,
                   status AS Status, booking_id AS BookingId, settlement_type AS SettlementType,
                   amount AS Amount, treasury_id AS TreasuryId,
                   target_booking_id AS TargetBookingId, reason AS Reason,
                   approved_by AS ApprovedBy, approved_at AS ApprovedAt,
                   journal_entry_id AS JournalEntryId, notes AS Notes, version AS Version
            FROM booking_settlements WHERE id = @id FOR UPDATE
            """, new { id }, tx, cancellationToken: ct)) ?? throw new NotFoundAppException();
        if (row.Status != CommercialStatuses.Draft)
            throw new ValidationAppException("validation.settlement_not_draft");
        if (row.Amount > 0 && row.ApprovedAt is null)
            throw new ValidationAppException("validation.settlement_not_approved");

        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT fn_assert_period_open(@docDate::date, @branchId, 'ACCOUNTING')",
            new { docDate = row.DocDate, branchId = row.BranchId }, tx, cancellationToken: ct));

        var customerId = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            "SELECT customer_id FROM bookings WHERE id = @id", new { id = row.BookingId }, tx,
            cancellationToken: ct));

        var docNo = await connection.ExecuteScalarAsync<string>(new CommandDefinition("""
            SELECT next_document_number('SETTLEMENT'::varchar, @branchId::uuid, @docDate::date,
                                        'SET'::varchar, 6::smallint)
            """, new { branchId = row.BranchId, docDate = row.DocDate }, tx,
            cancellationToken: ct));

        Guid? entryId = null;
        if (SettlementTypes.HasRefund(row.SettlementType) && row.Amount > 0)
            entryId = await PostRefundAsync(connection, tx, row, customerId, docNo, userId, ct);
        else if (row.SettlementType == SettlementTypes.Forfeit && row.Amount > 0)
            entryId = await PostForfeitAsync(connection, tx, row, customerId, docNo, userId, ct);

        if (SettlementTypes.HasCancel(row.SettlementType))
            await CancelReservationsAsync(connection, tx, row, ct);

        if (row.SettlementType == SettlementTypes.Transfer && row.TargetBookingId is not null)
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE bookings SET deposit_amount = deposit_amount + @amount, updated_by = @userId
                WHERE id = @targetBookingId
                """, new { amount = row.Amount, userId, targetBookingId = row.TargetBookingId },
                tx, cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE booking_settlements SET status = 'posted', doc_no = @docNo, posted_at = now(),
                posted_by = @userId, journal_entry_id = @entryId, updated_by = @userId
            WHERE id = @id
            """, new { docNo, userId, entryId, id }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return (await GetSettlementAsync(id, ct))!;
    }

    private static async Task<Guid?> PostRefundAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, SettlementRow row, Guid customerId, string docNo, Guid userId,
        CancellationToken ct)
    {
        if (row.TreasuryId is null)
            throw new ValidationAppException("validation.settlement_treasury_required");
        var treasury = await connection.QuerySingleOrDefaultAsync<(Guid AccountId, string Type)>(
            new CommandDefinition(
                "SELECT account_id, treasury_type FROM treasuries WHERE id = @id AND is_active",
                new { id = row.TreasuryId }, tx, cancellationToken: ct));
        if (treasury.AccountId == Guid.Empty)
            throw new ValidationAppException("validation.treasury_not_found");

        var defaults = await LoadDefaultsAsync(connection, tx, ct);
        var advances = Need(defaults, PostingKeys.CustomerAdvances, "sales");
        return await InsertEntryAsync(connection, tx, "cash", row.BranchId, row.DocDate, docNo,
            $"استرداد حجز {docNo}", new[]
            {
                new EntryLine(advances, row.Amount, 0, customerId),
                new EntryLine(treasury.AccountId, 0, row.Amount)
            }, userId, "voucher", null, ct);
    }

    private static async Task<Guid?> PostForfeitAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, SettlementRow row, Guid customerId, string docNo, Guid userId,
        CancellationToken ct)
    {
        var defaults = await LoadDefaultsAsync(connection, tx, ct);
        var advances = Need(defaults, PostingKeys.CustomerAdvances, "sales");
        var income = Need(defaults, PostingKeys.BookingForfeitIncome, "sales");
        return await InsertEntryAsync(connection, tx, "general", row.BranchId, row.DocDate, docNo,
            $"مصادرة حجز {docNo}", new[]
            {
                new EntryLine(advances, row.Amount, 0, customerId),
                new EntryLine(income, 0, row.Amount)
            }, userId, "voucher", null, ct);
    }

    private static async Task CancelReservationsAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, SettlementRow row, CancellationToken ct)
    {
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE stock_balances sb SET reserved_qty = GREATEST(sb.reserved_qty - l.reserved_qty, 0),
                updated_at = now()
            FROM booking_lines l
            WHERE l.booking_id = @bookingId AND l.reserved_qty > 0
              AND sb.item_id = l.item_id AND sb.warehouse_id = l.warehouse_id
            """, new { bookingId = row.BookingId }, tx, cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE booking_lines SET cancelled_qty = cancelled_qty + reserved_qty, " +
            "reserved_qty = 0 WHERE booking_id = @bookingId AND reserved_qty > 0",
            new { bookingId = row.BookingId }, tx, cancellationToken: ct));

        var delivered = await connection.ExecuteScalarAsync<decimal>(new CommandDefinition(
            "SELECT COALESCE(SUM(delivered_qty), 0) FROM booking_lines WHERE booking_id = @bookingId",
            new { bookingId = row.BookingId }, tx, cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE bookings SET status = @status WHERE id = @bookingId",
            new { status = delivered > 0 ? BookingStatuses.PartiallyDelivered
                : BookingStatuses.Cancelled, bookingId = row.BookingId }, tx, cancellationToken: ct));
    }
}
