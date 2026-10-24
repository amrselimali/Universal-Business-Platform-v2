using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IStockPostingRepository
{
    Task<StockDocument> PostAsync(Guid documentId, Guid userId, bool allowNegative,
        CancellationToken ct = default);
    Task<StockDocument> ReverseAsync(Guid documentId, Guid userId, bool allowNegative,
        CancellationToken ct = default);
    Task<StockDocument> ReceiveTransferAsync(TransferReceiptInput input, Guid userId,
        bool allowNegative, CancellationToken ct = default);
}

public sealed partial class StockPostingRepository : IStockPostingRepository
{
    private readonly IDbConnectionFactory _factory;

    public StockPostingRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<StockDocument> PostAsync(Guid documentId, Guid userId, bool allowNegative,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await PostCoreAsync(connection, tx, documentId, userId, allowNegative, ct);
        await tx.CommitAsync(ct);
        return (await GetDocumentAsync(connection, documentId, ct))!;
    }

    private static async Task<PostHeader> LockHeaderAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, Guid documentId, CancellationToken ct)
    {
        const string sql = """
            SELECT id AS Id, doc_type AS DocType, doc_date AS DocDate, branch_id AS BranchId,
                   warehouse_id AS WarehouseId, to_branch_id AS ToBranchId,
                   to_warehouse_id AS ToWarehouseId, status AS Status
            FROM stock_documents WHERE id = @documentId FOR UPDATE
            """;
        return await connection.QuerySingleOrDefaultAsync<PostHeader>(new CommandDefinition(
            sql, new { documentId }, tx, cancellationToken: ct)) ?? throw new NotFoundAppException();
    }

    internal static async Task<Guid> PostCoreAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        Guid documentId, Guid userId, bool allowNegative, CancellationToken ct)
    {
        var header = await LockHeaderAsync(connection, tx, documentId, ct);
        if (header.Status != StockStatuses.Draft)
            throw new ValidationAppException("validation.document_not_draft");

        if (await PeriodClosedAsync(connection, tx, header.DocDate, header.BranchId, ct))
            throw new ValidationAppException("validation.period_closed");

        var lines = (await connection.QueryAsync<PostLine>(new CommandDefinition("""
                SELECT id AS Id, line_no AS LineNo, direction AS Direction, item_id AS ItemId,
                       unit_id AS UnitId, quantity AS Quantity, base_quantity AS BaseQuantity,
                       unit_cost AS UnitCost, conversion_factor AS ConversionFactor,
                       lot_number AS LotNumber, expiry_date AS ExpiryDate
                FROM stock_document_lines WHERE document_id = @documentId ORDER BY line_no
                """, new { documentId }, tx, cancellationToken: ct))).ToList();
        if (lines.Count == 0)
            throw new ValidationAppException("validation.document_has_no_lines");

        var docNo = await connection.ExecuteScalarAsync<string>(new CommandDefinition("""
                SELECT next_document_number(@docType::varchar, @branchId::uuid, @docDate::date,
                                            NULL::varchar, 6::smallint)
                """, new { header.DocType, header.BranchId, header.DocDate }, tx, cancellationToken: ct));

        var periodId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
            SELECT id FROM fiscal_periods
            WHERE @docDate::date BETWEEN start_date AND end_date
            ORDER BY start_date DESC LIMIT 1
            """, new { header.DocDate }, tx, cancellationToken: ct));

        var total = 0m;
        foreach (var line in lines)
        {
            total += await ApplyLineAsync(connection, tx, header, line, periodId, userId,
                allowNegative, ct);
        }

        await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE stock_documents SET status = 'POSTED', doc_no = @docNo,
                    posted_at = now(), posted_by = @userId, total_cost = @total
                WHERE id = @documentId
                """, new { docNo, userId, total, documentId }, tx, cancellationToken: ct));

        return documentId;
    }

    private static async Task<decimal> ApplyLineAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, PostHeader header, PostLine line, Guid? periodId, Guid userId,
        bool allowNegative, CancellationToken ct)
    {
        var item = await connection.QuerySingleOrDefaultAsync<ItemRow>(new CommandDefinition(
            "SELECT item_type AS ItemType, is_active AS IsActive FROM items WHERE id = @itemId",
            new { line.ItemId }, tx, cancellationToken: ct)) ?? throw new ValidationAppException("validation.item_not_found");

        if (item.ItemType != "GOODS")
            throw new ValidationAppException("validation.service_item_not_stockable");
        if (!item.IsActive)
            throw new ValidationAppException("validation.item_inactive");

        if (!DirectionAllowed(header.DocType, line.Direction))
            throw new ValidationAppException("validation.document_direction_invalid");

        var quantity = line.BaseQuantity > 0 ? line.BaseQuantity : line.Quantity;
        var unitCost = line.UnitCost;
        if (line.Direction == "IN" && unitCost == 0)
        {
            unitCost = await connection.ExecuteScalarAsync<decimal?>(new CommandDefinition(
                "SELECT average_cost FROM stock_balances WHERE item_id = @itemId AND warehouse_id = @warehouseId",
                new { line.ItemId, header.WarehouseId }, tx, cancellationToken: ct)) ?? 0;
        }

        decimal moveCost;
        try
        {
            moveCost = await connection.ExecuteScalarAsync<decimal>(new CommandDefinition("""
                SELECT fn_stock_apply_lot(@itemId, @warehouseId, @direction, @quantity, @unitCost,
                                          @allowNegative, @lotNumber, @expiryDate::date, @documentId,
                                          @lineId, @preferGiven)
                """,
                new
                {
                    line.ItemId, header.WarehouseId, line.Direction, quantity, unitCost,
                    allowNegative, lotNumber = line.LotNumber, expiryDate = line.ExpiryDate,
                    documentId = header.Id, lineId = line.Id,
                    preferGiven = header.DocType == StockDocumentTypes.Reversal
                }, tx, cancellationToken: ct));
        }
        catch (PostgresException ex) when (ex.MessageText.StartsWith("NEGATIVE_STOCK", StringComparison.Ordinal))
        {
            throw new ValidationAppException("validation.negative_stock");
        }
        catch (PostgresException ex) when (ex.MessageText.StartsWith("EXPIRED_LOT", StringComparison.Ordinal))
        {
            throw new ValidationAppException("validation.expired_lot");
        }

        var actualUnitCost = quantity > 0 ? Math.Round(moveCost / quantity, 6) : unitCost;
        await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE stock_document_lines
                   SET unit_cost = @actualUnitCost, total_cost = @moveCost
                 WHERE id = @lineId
                """, new { actualUnitCost, moveCost, lineId = line.Id }, tx, cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO stock_movements
                    (document_id, document_line_id, item_id, warehouse_id, branch_id, direction,
                     quantity, unit_cost, total_cost, movement_date, period_id, created_by)
                VALUES
                    (@documentId, @lineId, @itemId, @warehouseId, @branchId, @direction,
                     @quantity, @actualUnitCost, @moveCost, @docDate, @periodId, @userId)
                """, new
        {
            documentId = header.Id, lineId = line.Id, line.ItemId, header.WarehouseId,
            header.BranchId, line.Direction, quantity, actualUnitCost, moveCost,
            header.DocDate, periodId, userId
        }, tx, cancellationToken: ct));

        return moveCost;
    }

    private static bool DirectionAllowed(string docType, string direction) => docType switch
    {
        StockDocumentTypes.Receipt or StockDocumentTypes.Opening => direction == "IN",
        StockDocumentTypes.Issue or StockDocumentTypes.Transfer => direction == "OUT",
        _ => direction is "IN" or "OUT"
    };

    internal static async Task<bool> PeriodClosedAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, DateTime date, Guid branchId, CancellationToken ct) =>
        await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT fn_period_is_closed(@date::date, @branchId, 'INVENTORY')",
            new { date, branchId }, tx, cancellationToken: ct));

    internal async Task<StockDocument?> GetDocumentAsync(NpgsqlConnection connection, Guid id,
        CancellationToken ct = default) =>
        await connection.QuerySingleOrDefaultAsync<StockDocument>(new CommandDefinition("""
            SELECT d.id AS Id, d.doc_no AS DocNo, d.doc_type AS DocType, d.doc_date AS DocDate,
                   d.branch_id AS BranchId, d.warehouse_id AS WarehouseId,
                   d.to_branch_id AS ToBranchId, d.to_warehouse_id AS ToWarehouseId,
                   d.status AS Status, d.notes AS Notes, d.total_cost AS TotalCost,
                   d.reversal_of_id AS ReversalOfId, d.posted_at AS PostedAt,
                   d.created_at AS CreatedAt, d.updated_at AS UpdatedAt, d.version AS Version
            FROM stock_documents d WHERE d.id = @id
            """, new { id }, cancellationToken: ct));

    private sealed class PostHeader
    {
        public Guid Id { get; set; }
        public string DocType { get; set; } = "";
        public DateTime DocDate { get; set; }
        public Guid BranchId { get; set; }
        public Guid WarehouseId { get; set; }
        public Guid? ToBranchId { get; set; }
        public Guid? ToWarehouseId { get; set; }
        public string Status { get; set; } = "";
    }

    private sealed class PostLine
    {
        public Guid Id { get; set; }
        public short LineNo { get; set; }
        public string Direction { get; set; } = "IN";
        public Guid ItemId { get; set; }
        public Guid UnitId { get; set; }
        public decimal Quantity { get; set; }
        public decimal BaseQuantity { get; set; }
        public decimal UnitCost { get; set; }
        public decimal ConversionFactor { get; set; }
        public string? LotNumber { get; set; }
        public DateTime? ExpiryDate { get; set; }
    }

    private sealed class ItemRow
    {
        public string ItemType { get; set; } = "GOODS";
        public bool IsActive { get; set; }
    }
}
