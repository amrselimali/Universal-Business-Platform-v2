using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IBookingRepository
{
    Task<Guid> CreateDraftAsync(Guid id, BookingInput input, Guid userId, CancellationToken ct = default);
    Task<Booking> UpdateDraftAsync(Guid id, int version, BookingInput input, Guid userId,
        CancellationToken ct = default);
    Task<Booking?> GetAsync(Guid id, CancellationToken ct = default);
    Task<PagedResult<Booking>> ListAsync(BookingFilter filter, PageRequest request,
        CancellationToken ct = default);
    Task<Booking> ConfirmAsync(Guid id, Guid userId, bool reserveStock, CancellationToken ct = default);
    Task<Booking> CancelAsync(Guid id, string reason, Guid userId, CancellationToken ct = default);
    Task<Booking> AssignEmployeeAsync(Guid id, Guid employeeId, string role, Guid userId,
        CancellationToken ct = default);
    Task<decimal> AvailableAsync(Guid itemId, Guid warehouseId, CancellationToken ct = default);
}

/// <summary>الحجوزات: مسودة ← تأكيد (حجز المتاح) ← تنفيذ يحدّث الكميات المنجزة.</summary>
public sealed partial class BookingRepository : IBookingRepository
{
    private readonly IDbConnectionFactory _factory;
    public BookingRepository(IDbConnectionFactory factory) => _factory = factory;

    private sealed class BookingRow
    {
        public Guid Id { get; set; }
        public Guid BranchId { get; set; }
        public string? DocNo { get; set; }
        public DateTime BookingDate { get; set; }
        public string Status { get; set; } = BookingStatuses.Draft;
        public Guid CustomerId { get; set; }
        public string? CustomerName { get; set; }
        public string Source { get; set; } = BookingSources.Manual;
        public DateTime? DueDate { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public Guid? AssignedEmployeeId { get; set; }
        public string? EmployeeName { get; set; }
        public Guid? RelatedBookingId { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal DepositAmount { get; set; }
        public string? Notes { get; set; }
        public int Version { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    private sealed class LineRow
    {
        public Guid Id { get; set; }
        public int LineNo { get; set; }
        public Guid ItemId { get; set; }
        public string? ItemCode { get; set; }
        public string? ItemName { get; set; }
        public Guid UnitId { get; set; }
        public string? UnitName { get; set; }
        public Guid WarehouseId { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal DiscountPercent { get; set; }
        public Guid? TaxId { get; set; }
        public decimal TaxRate { get; set; }
        public decimal NetAmount { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal DeliveredQty { get; set; }
        public decimal InvoicedQty { get; set; }
        public decimal CancelledQty { get; set; }
        public decimal ReservedQty { get; set; }
    }

    private const string HeadSelect = """
        SELECT b.id AS Id, b.branch_id AS BranchId, b.doc_no AS DocNo, b.booking_date AS BookingDate,
               b.status AS Status, b.customer_id AS CustomerId, c.name_ar AS CustomerName,
               b.source AS Source, b.due_date AS DueDate, b.expires_at AS ExpiresAt,
               b.assigned_employee_id AS AssignedEmployeeId, e.name_ar AS EmployeeName,
               b.related_booking_id AS RelatedBookingId, b.total_amount AS TotalAmount,
               b.deposit_amount AS DepositAmount, b.notes AS Notes, b.version AS Version,
               b.created_at AS CreatedAt
        FROM bookings b
        JOIN customers c ON c.id = b.customer_id
        LEFT JOIN employees e ON e.id = b.assigned_employee_id
        """;

    public async Task<Booking?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var head = await connection.QuerySingleOrDefaultAsync<BookingRow>(new CommandDefinition(
            HeadSelect + " WHERE b.id = @id", new { id }, cancellationToken: ct));
        if (head is null) return null;

        var booking = ToBooking(head);
        booking.Lines = (await ListLinesAsync(connection, id, ct)).ToList();
        booking.Assignments = (await connection.QueryAsync<BookingAssignment>(new CommandDefinition("""
            SELECT a.id AS Id, a.employee_id AS EmployeeId, e.name_ar AS EmployeeName, a.role AS Role,
                   a.assigned_at AS AssignedAt, a.unassigned_at AS UnassignedAt
            FROM booking_assignments a JOIN employees e ON e.id = a.employee_id
            WHERE a.booking_id = @id ORDER BY a.assigned_at DESC
            """, new { id }, cancellationToken: ct))).ToList();
        return booking;
    }

    public async Task<PagedResult<Booking>> ListAsync(BookingFilter filter, PageRequest request,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var where = new List<string>();
        if (filter.BranchId.HasValue) where.Add("b.branch_id = @branchId");
        if (!string.IsNullOrWhiteSpace(filter.Status)) where.Add("b.status = @status");
        if (filter.CustomerId.HasValue) where.Add("b.customer_id = @customerId");
        if (!string.IsNullOrWhiteSpace(filter.Search))
            where.Add("(b.doc_no ILIKE '%' || @search || '%' OR c.name_ar ILIKE '%' || @search || '%')");
        var whereSql = where.Count == 0 ? "WHERE true" : "WHERE " + string.Join(" AND ", where);
        var args = new { branchId = filter.BranchId, status = filter.Status,
            customerId = filter.CustomerId, search = filter.Search, take = request.SafePageSize,
            offset = request.Offset };

        var items = await connection.QueryAsync<BookingRow>(new CommandDefinition(
            HeadSelect + " " + whereSql + " ORDER BY b.booking_date DESC, b.id DESC LIMIT @take OFFSET @offset",
            args, cancellationToken: ct));
        var total = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM bookings b JOIN customers c ON c.id = b.customer_id " + whereSql,
            args, cancellationToken: ct));

        return new PagedResult<Booking>
        {
            Items = items.Select(ToBooking).ToList(), Total = total,
            Page = request.SafePage, PageSize = request.SafePageSize
        };
    }

    public async Task<decimal> AvailableAsync(Guid itemId, Guid warehouseId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<decimal>(new CommandDefinition(
            "SELECT COALESCE(fn_stock_available(@itemId, @warehouseId), 0)",
            new { itemId, warehouseId }, cancellationToken: ct));
    }

    private static Booking ToBooking(BookingRow row) => new()
    {
        Id = row.Id, BranchId = row.BranchId, DocNo = row.DocNo, BookingDate = row.BookingDate,
        Status = row.Status, CustomerId = row.CustomerId, CustomerName = row.CustomerName,
        Source = row.Source, DueDate = row.DueDate, ExpiresAt = row.ExpiresAt,
        AssignedEmployeeId = row.AssignedEmployeeId, EmployeeName = row.EmployeeName,
        RelatedBookingId = row.RelatedBookingId, TotalAmount = row.TotalAmount,
        DepositAmount = row.DepositAmount, Notes = row.Notes, Version = row.Version,
        CreatedAt = row.CreatedAt
    };

    private static async Task<IReadOnlyList<BookingLine>> ListLinesAsync(NpgsqlConnection connection,
        Guid bookingId, CancellationToken ct) =>
        (await connection.QueryAsync<LineRow>(new CommandDefinition("""
            SELECT l.id AS Id, l.line_no AS LineNo, l.item_id AS ItemId, i.code AS ItemCode,
                   i.name_ar AS ItemName, l.unit_id AS UnitId, u.name_ar AS UnitName,
                   l.warehouse_id AS WarehouseId, l.quantity AS Quantity, l.unit_price AS UnitPrice,
                   l.discount_percent AS DiscountPercent, l.tax_id AS TaxId,
                   COALESCE(t.rate, 0) AS TaxRate, l.net_amount AS NetAmount,
                   l.tax_amount AS TaxAmount, l.total_amount AS TotalAmount,
                   l.delivered_qty AS DeliveredQty, l.invoiced_qty AS InvoicedQty,
                   l.cancelled_qty AS CancelledQty, l.reserved_qty AS ReservedQty
            FROM booking_lines l
            JOIN items i ON i.id = l.item_id
            JOIN units u ON u.id = l.unit_id
            LEFT JOIN taxes t ON t.id = l.tax_id
            WHERE l.booking_id = @bookingId ORDER BY l.line_no
            """, new { bookingId }, cancellationToken: ct)))
        .Select(l => new BookingLine
        {
            Id = l.Id, LineNo = l.LineNo, ItemId = l.ItemId, ItemCode = l.ItemCode,
            ItemName = l.ItemName, UnitId = l.UnitId, UnitName = l.UnitName,
            WarehouseId = l.WarehouseId, Quantity = l.Quantity, UnitPrice = l.UnitPrice,
            DiscountPercent = l.DiscountPercent, TaxId = l.TaxId, TaxRate = l.TaxRate,
            NetAmount = l.NetAmount, TaxAmount = l.TaxAmount, TotalAmount = l.TotalAmount,
            DeliveredQty = l.DeliveredQty, InvoicedQty = l.InvoicedQty,
            CancelledQty = l.CancelledQty, ReservedQty = l.ReservedQty
        }).ToList();
}
