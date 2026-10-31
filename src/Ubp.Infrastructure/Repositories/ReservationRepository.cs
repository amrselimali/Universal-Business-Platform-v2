using Dapper;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IReservationRepository
{
    Task<PagedResult<ReservationRow>> ListAsync(PageRequest request, IReadOnlyCollection<Guid> branchIds,
        Guid? customerId, bool onlyRemaining, CancellationToken ct = default);
    Task<IReadOnlyList<ReservationRow>> RemainingLinesAsync(Guid bookingId,
        CancellationToken ct = default);
    Task<bool> AdvanceExistsAsync(Guid bookingId, CancellationToken ct = default);
    Task<Guid?> InsertAdjustmentAsync(Guid bookingId, Guid? bookingLineId, string kind,
        decimal quantity, decimal amount, Guid? voucherId, Guid? journalEntryId, string? note,
        Guid userId, CancellationToken ct = default);
    Task<bool> ExtendAsync(Guid bookingId, DateTimeOffset newExpiry, CancellationToken ct = default);
}

/// <summary>
/// قراءة الحجوزات من <c>v_customer_reservations</c> (مصدر واحد للكميات المحجوزة/المستهلكة/المتبقية)،
/// وسجل التحويلات التي تمنع تحويل نفس السطر مرتين.
/// </summary>
public sealed class ReservationRepository : IReservationRepository
{
    private const string Columns = """
        booking_id AS BookingId, branch_id AS BranchId, customer_id AS CustomerId,
        customer_name AS CustomerName, booking_status AS BookingStatus, booking_no AS BookingNo,
        deposit_amount AS DepositAmount, expires_at AS ExpiresAt, booking_line_id AS BookingLineId,
        item_id AS ItemId, item_code AS ItemCode, item_name AS ItemName,
        warehouse_id AS WarehouseId, warehouse_name AS WarehouseName, booked_qty AS BookedQty,
        reserved_qty AS ReservedQty, consumed_qty AS ConsumedQty, remaining_qty AS RemainingQty,
        unit_price AS UnitPrice, remaining_value AS RemainingValue
        """;

    private const string Filter = """
        (@allBranches OR branch_id = ANY(@branchIds))
        AND (@customerId IS NULL OR customer_id = @customerId)
        AND (NOT @onlyRemaining OR remaining_qty > 0)
        """;

    private readonly IDbConnectionFactory _factory;

    public ReservationRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<PagedResult<ReservationRow>> ListAsync(PageRequest request,
        IReadOnlyCollection<Guid> branchIds, Guid? customerId, bool onlyRemaining,
        CancellationToken ct = default)
    {
        var sql = $"""
            SELECT {Columns} FROM v_customer_reservations
            WHERE {Filter}
            ORDER BY booking_no NULLS LAST, booking_line_id
            LIMIT @take OFFSET @offset;
            SELECT count(*)::int FROM v_customer_reservations WHERE {Filter};
            """;
        await using var connection = await _factory.OpenAsync(ct);
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, new
        {
            branchIds = branchIds.ToArray(),
            allBranches = branchIds.Count == 0,
            customerId,
            onlyRemaining,
            take = request.SafePageSize,
            offset = request.Offset
        }, cancellationToken: ct));

        var items = (await multi.ReadAsync<ReservationRow>()).ToList();
        var total = await multi.ReadFirstAsync<int>();
        return new PagedResult<ReservationRow>
        {
            Items = items, Total = total, Page = request.SafePage, PageSize = request.SafePageSize
        };
    }

    public async Task<IReadOnlyList<ReservationRow>> RemainingLinesAsync(Guid bookingId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<ReservationRow>(new CommandDefinition(
            $"SELECT {Columns} FROM v_customer_reservations " +
            "WHERE booking_id = @bookingId AND remaining_qty > 0 ORDER BY booking_line_id",
            new { bookingId }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<bool> AdvanceExistsAsync(Guid bookingId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
            SELECT EXISTS (SELECT 1 FROM reservation_adjustments
                           WHERE booking_id = @bookingId AND kind = 'TO_ADVANCE')
            """, new { bookingId }, cancellationToken: ct));
    }

    public async Task<Guid?> InsertAdjustmentAsync(Guid bookingId, Guid? bookingLineId, string kind,
        decimal quantity, decimal amount, Guid? voucherId, Guid? journalEntryId, string? note,
        Guid userId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
            INSERT INTO reservation_adjustments
                (booking_id, booking_line_id, kind, quantity, amount, voucher_id,
                 journal_entry_id, note, created_by)
            VALUES (@bookingId, @bookingLineId, @kind, @quantity, @amount, @voucherId,
                    @journalEntryId, @note, @userId)
            ON CONFLICT DO NOTHING
            RETURNING id
            """, new { bookingId, bookingLineId, kind, quantity, amount, voucherId,
                journalEntryId, note, userId }, cancellationToken: ct));
    }

    public async Task<bool> ExtendAsync(Guid bookingId, DateTimeOffset newExpiry,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE bookings SET expires_at = @newExpiry
            WHERE id = @bookingId
              AND status IN ('draft', 'confirmed', 'partially_delivered')
            """, new { bookingId, newExpiry }, cancellationToken: ct)) > 0;
    }
}
