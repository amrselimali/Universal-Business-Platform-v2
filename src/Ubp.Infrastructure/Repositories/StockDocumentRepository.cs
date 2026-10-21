using Dapper;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IStockDocumentRepository
{
    Task<PagedResult<StockDocument>> ListAsync(PageRequest request, StockDocumentFilter filter,
        bool isAdmin, IReadOnlyCollection<Guid> branchIds, CancellationToken ct = default);
    Task<StockDocument?> GetAsync(Guid id, CancellationToken ct = default);
    Task<StockDocumentDetail?> GetDetailAsync(Guid id, CancellationToken ct = default);
    Task<StockDocument> CreateDraftAsync(StockDocumentInput input, Guid userId, CancellationToken ct = default);
    Task<StockDocument?> UpdateDraftAsync(Guid id, int version, StockDocumentInput input,
        CancellationToken ct = default);
    Task<bool> DeleteDraftAsync(Guid id, CancellationToken ct = default);
    Task CancelAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<TransferReceipt>> ListReceiptsAsync(Guid documentId, CancellationToken ct = default);
    Task<bool> WarehouseLinkedAsync(Guid warehouseId, Guid branchId, CancellationToken ct = default);
    Task LinkStocktakeAsync(Guid documentId, Guid stocktakeId, CancellationToken ct = default);
}

public sealed partial class StockDocumentRepository : IStockDocumentRepository
{
    private const string Columns = """
        d.id AS Id, d.doc_no AS DocNo, d.doc_type AS DocType, d.doc_date AS DocDate,
        d.branch_id AS BranchId,
        (SELECT b.name_ar FROM branches b WHERE b.id = d.branch_id) AS BranchName,
        d.warehouse_id AS WarehouseId,
        (SELECT w.name_ar FROM warehouses w WHERE w.id = d.warehouse_id) AS WarehouseName,
        d.to_branch_id AS ToBranchId,
        (SELECT b.name_ar FROM branches b WHERE b.id = d.to_branch_id) AS ToBranchName,
        d.to_warehouse_id AS ToWarehouseId,
        (SELECT w.name_ar FROM warehouses w WHERE w.id = d.to_warehouse_id) AS ToWarehouseName,
        d.status AS Status, d.notes AS Notes, d.total_cost AS TotalCost,
        d.reversal_of_id AS ReversalOfId,
        EXISTS (SELECT 1 FROM stock_documents r WHERE r.reversal_of_id = d.id) AS IsReversed,
        d.stocktake_id AS StocktakeId, d.posted_at AS PostedAt,
        (SELECT u.username FROM users u WHERE u.id = d.posted_by) AS PostedByName,
        d.created_at AS CreatedAt, d.updated_at AS UpdatedAt, d.version AS Version,
        (SELECT count(*)::int FROM stock_document_lines l WHERE l.document_id = d.id) AS LinesCount
        """;

    private static readonly IReadOnlyDictionary<string, string> SortMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["doc_date"] = "d.doc_date", ["doc_no"] = "d.doc_no",
            ["created_at"] = "d.created_at", ["status"] = "d.status"
        };

    private readonly IDbConnectionFactory _factory;

    public StockDocumentRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<PagedResult<StockDocument>> ListAsync(PageRequest request,
        StockDocumentFilter filter, bool isAdmin, IReadOnlyCollection<Guid> branchIds,
        CancellationToken ct = default)
    {
        var where = new List<string>();
        if (!isAdmin) where.Add("d.branch_id = ANY(@branchIds)");
        if (!string.IsNullOrWhiteSpace(filter.DocType)) where.Add("d.doc_type = @docType");
        if (!string.IsNullOrWhiteSpace(filter.Status)) where.Add("d.status = @status");
        if (filter.From.HasValue) where.Add("d.doc_date >= @from");
        if (filter.To.HasValue) where.Add("d.doc_date <= @to");
        if (filter.WarehouseId.HasValue)
            where.Add("(d.warehouse_id = @warehouseId OR d.to_warehouse_id = @warehouseId)");
        if (!string.IsNullOrWhiteSpace(request.Search))
            where.Add("(coalesce(d.doc_no,'') ILIKE '%' || @search || '%' OR coalesce(d.notes,'') ILIKE '%' || @search || '%')");

        var whereSql = where.Count == 0 ? "WHERE true" : "WHERE " + string.Join(" AND ", where);
        var order = SqlSort.Build(request.SortBy, SortMap, "d.doc_date", request.Descending);
        var sql = $"""
            SELECT {Columns} FROM stock_documents d {whereSql}
            ORDER BY {order} LIMIT @take OFFSET @offset;
            SELECT count(*) FROM stock_documents d {whereSql};
            """;

        await using var connection = await _factory.OpenAsync(ct);
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, new
        {
            branchIds = branchIds.ToArray(), filter.DocType, filter.Status,
            filter.From, filter.To, filter.WarehouseId, search = request.Search,
            take = request.SafePageSize, offset = request.Offset
        }, cancellationToken: ct));
        var items = (await multi.ReadAsync<StockDocument>()).ToList();
        var total = await multi.ReadFirstAsync<int>();
        return new PagedResult<StockDocument>
        {
            Items = items, Total = total, Page = request.SafePage, PageSize = request.SafePageSize
        };
    }

    public async Task<StockDocument?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<StockDocument>(new CommandDefinition(
            $"SELECT {Columns} FROM stock_documents d WHERE d.id = @id", new { id }, cancellationToken: ct));
    }

    public async Task<StockDocumentDetail?> GetDetailAsync(Guid id, CancellationToken ct = default)
    {
        var document = await GetAsync(id, ct);
        if (document is null) return null;

        const string linesSql = """
            SELECT l.id AS Id, l.document_id AS DocumentId, l.line_no AS LineNo,
                   l.direction AS Direction, l.item_id AS ItemId,
                   (SELECT i.code FROM items i WHERE i.id = l.item_id) AS ItemCode,
                   (SELECT i.name_ar FROM items i WHERE i.id = l.item_id) AS ItemName,
                   l.unit_id AS UnitId,
                   (SELECT u.name_ar FROM units u WHERE u.id = l.unit_id) AS UnitName,
                   l.conversion_factor AS ConversionFactor, l.quantity AS Quantity,
                   l.base_quantity AS BaseQuantity, l.unit_cost AS UnitCost,
                   l.total_cost AS TotalCost, l.notes AS Notes,
                   COALESCE((SELECT sum(rl.quantity) FROM stock_transfer_receipt_lines rl
                             WHERE rl.document_line_id = l.id), 0) AS ReceivedQuantity
            FROM stock_document_lines l
            WHERE l.document_id = @id
            ORDER BY l.line_no
            """;

        await using var connection = await _factory.OpenAsync(ct);
        var lines = (await connection.QueryAsync<StockDocumentLine>(new CommandDefinition(
            linesSql, new { id }, cancellationToken: ct))).ToList();
        var receipts = await ListReceiptsAsync(id, ct);
        return new StockDocumentDetail { Document = document, Lines = lines, Receipts = receipts };
    }

    public async Task<bool> WarehouseLinkedAsync(Guid warehouseId, Guid branchId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM warehouse_branches WHERE warehouse_id = @warehouseId AND branch_id = @branchId)",
            new { warehouseId, branchId }, cancellationToken: ct));
    }

    public async Task LinkStocktakeAsync(Guid documentId, Guid stocktakeId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE stock_documents SET stocktake_id = @stocktakeId WHERE id = @documentId AND status = 'DRAFT'",
            new { documentId, stocktakeId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<TransferReceipt>> ListReceiptsAsync(Guid documentId,
        CancellationToken ct = default)
    {
        const string receiptsSql = """
            SELECT r.id AS Id, r.document_id AS DocumentId, r.received_at AS ReceivedAt,
                   (SELECT u.username FROM users u WHERE u.id = r.received_by) AS ReceivedByName,
                   r.notes AS Notes
            FROM stock_transfer_receipts r
            WHERE r.document_id = @documentId ORDER BY r.received_at DESC
            """;
        const string linesSql = """
            SELECT rl.id AS Id, rl.receipt_id AS ReceiptId, rl.document_line_id AS DocumentLineId,
                   (SELECT i.name_ar FROM items i
                    JOIN stock_document_lines l ON l.item_id = i.id
                    WHERE l.id = rl.document_line_id) AS ItemName,
                   rl.quantity AS Quantity
            FROM stock_transfer_receipt_lines rl WHERE rl.receipt_id = @receiptId
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var receipts = (await connection.QueryAsync<TransferReceipt>(new CommandDefinition(
            receiptsSql, new { documentId }, cancellationToken: ct))).ToList();
        foreach (var receipt in receipts)
        {
            var lines = await connection.QueryAsync<TransferReceiptLine>(new CommandDefinition(
                linesSql, new { receiptId = receipt.Id }, cancellationToken: ct));
            receipt.Lines = lines.ToList();
        }
        return receipts;
    }
}
