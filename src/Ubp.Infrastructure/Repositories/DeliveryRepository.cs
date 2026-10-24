using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IDeliveryRepository
{
    Task<Guid> CreateDraftAsync(Guid id, DeliveryInput input, Guid userId,
        CancellationToken ct = default);
    Task<DeliveryNote?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<DeliveryNote>> ListAsync(Guid? branchId, string? status, int limit,
        CancellationToken ct = default);
    Task<DeliveryNote> PostAsync(Guid id, Guid userId, bool allowNegative,
        CancellationToken ct = default);
    Task<bool> DeleteDraftAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<DeliveryNote>> ListByBookingAsync(Guid bookingId,
        CancellationToken ct = default);
}

/// <summary>إذن التنفيذ: يخصم المخزون FEFO ويثبت التكلفة (N4) ويحدّث كميات الحجز.</summary>
public sealed partial class DeliveryRepository : IDeliveryRepository
{
    private readonly IDbConnectionFactory _factory;
    public DeliveryRepository(IDbConnectionFactory factory) => _factory = factory;

    private sealed class DeliveryRow
    {
        public Guid Id { get; set; }
        public Guid BranchId { get; set; }
        public string? DocNo { get; set; }
        public DateTime DocDate { get; set; }
        public string Status { get; set; } = DeliveryStatuses.Draft;
        public Guid BookingId { get; set; }
        public string? BookingNo { get; set; }
        public Guid WarehouseId { get; set; }
        public string? WarehouseName { get; set; }
        public Guid? StockDocumentId { get; set; }
        public Guid? JournalEntryId { get; set; }
        public string? Notes { get; set; }
        public int Version { get; set; }
    }

    private sealed class DeliveryLineRow
    {
        public Guid Id { get; set; }
        public int LineNo { get; set; }
        public Guid BookingLineId { get; set; }
        public Guid ItemId { get; set; }
        public string? ItemName { get; set; }
        public Guid UnitId { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitCost { get; set; }
        public decimal CostTotal { get; set; }
    }

    private const string HeadSelect = """
        SELECT d.id AS Id, d.branch_id AS BranchId, d.doc_no AS DocNo, d.doc_date AS DocDate,
               d.status AS Status, d.booking_id AS BookingId, b.doc_no AS BookingNo,
               d.warehouse_id AS WarehouseId, w.name_ar AS WarehouseName,
               d.stock_document_id AS StockDocumentId, d.journal_entry_id AS JournalEntryId,
               d.notes AS Notes, d.version AS Version
        FROM delivery_notes d
        JOIN bookings b ON b.id = d.booking_id
        JOIN warehouses w ON w.id = d.warehouse_id
        """;

    public async Task<Guid> CreateDraftAsync(Guid id, DeliveryInput input, Guid userId,
        CancellationToken ct = default)
    {
        if (input.Lines.Count == 0)
            throw new ValidationAppException("validation.document_has_no_lines");

        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var booking = await connection.QuerySingleOrDefaultAsync<(Guid Id, Guid BranchId, string Status)>(
            new CommandDefinition("SELECT id, branch_id, status FROM bookings WHERE id = @id FOR UPDATE",
                new { id = input.BookingId }, tx, cancellationToken: ct));
        if (booking.Id == Guid.Empty) throw new NotFoundAppException();
        if (booking.Status is not (BookingStatuses.Confirmed or BookingStatuses.PartiallyDelivered))
            throw new ValidationAppException("validation.booking_not_confirmed");
        if (booking.BranchId != input.BranchId)
            throw new ValidationAppException("validation.booking_branch_mismatch");

        var bookingLines = (await connection.QueryAsync<(Guid Id, Guid ItemId, Guid UnitId,
            Guid WarehouseId, decimal Remaining)>(new CommandDefinition("""
            SELECT id, item_id, unit_id, warehouse_id,
                   quantity - delivered_qty - cancelled_qty AS remaining
            FROM booking_lines WHERE booking_id = @bookingId
            """, new { bookingId = input.BookingId }, tx, cancellationToken: ct)))
            .ToDictionary(x => x.Id);

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO delivery_notes (id, branch_id, doc_date, status, booking_id, warehouse_id,
                notes, created_by)
            VALUES (@id, @BranchId, @docDate, 'draft', @BookingId, @WarehouseId, @Notes, @userId)
            """, new { id, input.BranchId, docDate = input.DocDate.Date, input.BookingId,
                input.WarehouseId, input.Notes, userId }, tx, cancellationToken: ct));

        var lineNo = 0;
        foreach (var line in input.Lines)
        {
            lineNo++;
            if (!bookingLines.TryGetValue(line.BookingLineId, out var source))
                throw new ValidationAppException("validation.booking_line_not_found");
            if (line.Quantity <= 0 || line.Quantity > source.Remaining)
                throw new ValidationAppException("validation.delivery_exceeds_booking");
            if (source.WarehouseId != input.WarehouseId)
                throw new ValidationAppException("validation.delivery_warehouse_mismatch");

            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO delivery_note_lines (delivery_note_id, line_no, booking_line_id, item_id,
                    unit_id, quantity)
                VALUES (@id, @lineNo, @BookingLineId, @itemId, @unitId, @quantity)
                """, new { id, lineNo, line.BookingLineId, itemId = source.ItemId,
                    unitId = source.UnitId, quantity = line.Quantity }, tx, cancellationToken: ct));
        }

        await tx.CommitAsync(ct);
        return id;
    }

    public async Task<DeliveryNote?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var head = await connection.QuerySingleOrDefaultAsync<DeliveryRow>(new CommandDefinition(
            HeadSelect + " WHERE d.id = @id", new { id }, cancellationToken: ct));
        if (head is null) return null;

        var note = ToNote(head);
        note.Lines = (await connection.QueryAsync<DeliveryLineRow>(new CommandDefinition("""
            SELECT l.id AS Id, l.line_no AS LineNo, l.booking_line_id AS BookingLineId,
                   l.item_id AS ItemId, i.name_ar AS ItemName, l.unit_id AS UnitId,
                   l.quantity AS Quantity, l.unit_cost AS UnitCost, l.cost_total AS CostTotal
            FROM delivery_note_lines l JOIN items i ON i.id = l.item_id
            WHERE l.delivery_note_id = @id ORDER BY l.line_no
            """, new { id }, cancellationToken: ct)))
            .Select(l => new DeliveryNoteLine
            {
                Id = l.Id, LineNo = l.LineNo, BookingLineId = l.BookingLineId, ItemId = l.ItemId,
                ItemName = l.ItemName, UnitId = l.UnitId, Quantity = l.Quantity,
                UnitCost = l.UnitCost, CostTotal = l.CostTotal
            }).ToList();
        return note;
    }

    public async Task<IReadOnlyList<DeliveryNote>> ListAsync(Guid? branchId, string? status,
        int limit, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<DeliveryRow>(new CommandDefinition(
            HeadSelect + """
             WHERE (@branchId::uuid IS NULL OR d.branch_id = @branchId::uuid)
               AND (@status::varchar IS NULL OR d.status = @status::varchar)
             ORDER BY d.doc_date DESC, d.id DESC
             LIMIT @limit
            """, new { branchId, status, limit = Math.Clamp(limit, 1, 500) }, cancellationToken: ct));
        return rows.Select(ToNote).ToList();
    }

    public async Task<IReadOnlyList<DeliveryNote>> ListByBookingAsync(Guid bookingId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<DeliveryRow>(new CommandDefinition(
            HeadSelect + " WHERE d.booking_id = @bookingId ORDER BY d.doc_date, d.id",
            new { bookingId }, cancellationToken: ct));
        return rows.Select(ToNote).ToList();
    }

    public async Task<bool> DeleteDraftAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM delivery_notes WHERE id = @id AND status = 'draft'", new { id },
            cancellationToken: ct)) > 0;
    }

    private static DeliveryNote ToNote(DeliveryRow row) => new()
    {
        Id = row.Id, BranchId = row.BranchId, DocNo = row.DocNo, DocDate = row.DocDate,
        Status = row.Status, BookingId = row.BookingId, BookingNo = row.BookingNo,
        WarehouseId = row.WarehouseId, WarehouseName = row.WarehouseName,
        StockDocumentId = row.StockDocumentId, JournalEntryId = row.JournalEntryId,
        Notes = row.Notes, Version = row.Version
    };
}
