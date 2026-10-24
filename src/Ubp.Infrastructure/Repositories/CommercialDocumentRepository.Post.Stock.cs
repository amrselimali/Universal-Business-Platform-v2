using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class CommercialDocumentRepository
{
    /// <summary>التحقق والحسابات ثم مستند المخزون الداخلي (يُدخل الدفعات عبر محرك FEFO).</summary>
    private async Task<PostContext> PrepareAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        Guid id, Guid userId, bool allowNegative, CancellationToken ct)
    {
        var head = await connection.QuerySingleOrDefaultAsync<PostHead>(new CommandDefinition("""
            SELECT id AS Id, doc_type AS DocType, doc_date AS DocDate, branch_id AS BranchId,
                   warehouse_id AS WarehouseId, supplier_id AS SupplierId, status AS Status, tax_total AS TaxTotal,
                   total AS Total
            FROM commercial_documents WHERE id = @id FOR UPDATE
            """, new { id }, tx, cancellationToken: ct)) ?? throw new NotFoundAppException();

        if (head.Status != CommercialStatuses.Draft)
            throw new ValidationAppException("validation.document_not_draft");
        if (!CommercialDocTypes.IsPurchase(head.DocType))
            throw new ValidationAppException("validation.not_supported_document");
        if (head.WarehouseId is null)
            throw new ValidationAppException("validation.warehouse_required");

        var lines = (await connection.QueryAsync<PostLine>(new CommandDefinition("""
            SELECT line_no AS LineNo, item_id AS ItemId, unit_id AS UnitId,
                   conversion_factor AS ConversionFactor, quantity AS Quantity,
                   net_amount AS NetAmount, lot_number AS LotNumber, expiry_date AS ExpiryDate
            FROM commercial_document_lines WHERE document_id = @id ORDER BY line_no
            """, new { id }, tx, cancellationToken: ct))).ToList();
        if (lines.Count == 0)
            throw new ValidationAppException("validation.document_has_no_lines");

        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT fn_assert_period_open(@docDate::date, @branchId, 'ACCOUNTING')",
            new { docDate = head.DocDate, branchId = head.BranchId }, tx, cancellationToken: ct));

        var inventory = (await connection.QueryAsync<(int LineNo, Guid? AccountId)>(
            new CommandDefinition("""
                SELECT l.line_no, m.inventory_account_id FROM commercial_document_lines l
                JOIN items i ON i.id = l.item_id
                LEFT JOIN item_category_accounts m ON m.category_id = i.category_id
                WHERE l.document_id = @id ORDER BY l.line_no
                """, new { id }, tx, cancellationToken: ct))).ToList();
        if (inventory.Any(x => x.AccountId is null))
            throw new ValidationAppException("validation.stock_accounts_missing");

        var defaults = (await connection.QueryAsync<(string Key, Guid? AccountId)>(
            new CommandDefinition("SELECT setting_key, account_id FROM posting_defaults",
                transaction: tx, cancellationToken: ct)))
            .Where(r => r.AccountId.HasValue)
            .ToDictionary(r => r.Key, r => r.AccountId!.Value, StringComparer.OrdinalIgnoreCase);

        var isReturn = CommercialDocTypes.IsReturn(head.DocType);
        var docNo = await connection.ExecuteScalarAsync<string>(new CommandDefinition("""
            SELECT next_document_number(@docType::varchar, @branchId::uuid, @docDate::date,
                                        NULL::varchar, 6::smallint)
            """, new
        {
            docType = CommercialDocTypes.SequenceName(head.DocType),
            branchId = head.BranchId, docDate = head.DocDate
        }, tx, cancellationToken: ct));

        var stockDocId = await CreateStockDocumentAsync(connection, tx, head, lines, isReturn,
            docNo, userId, allowNegative, ct);

        var costs = (await connection.QueryAsync<(int LineNo, decimal TotalCost)>(
            new CommandDefinition(
                "SELECT line_no, total_cost FROM stock_document_lines WHERE document_id = @stockDocId",
                new { stockDocId }, tx, cancellationToken: ct)))
            .ToDictionary(x => x.LineNo, x => x.TotalCost);

        return new PostContext
        {
            Id = id, SupplierId = head.SupplierId, DocType = head.DocType, DocDate = head.DocDate, BranchId = head.BranchId,
            WarehouseId = head.WarehouseId, TaxTotal = head.TaxTotal, Total = head.Total,
            IsReturn = isReturn, DocNo = docNo, StockDocumentId = stockDocId,
            EntryId = Guid.CreateVersion7(), Lines = lines, Costs = costs, Defaults = defaults,
            Inventory = inventory.ToDictionary(x => x.LineNo, x => x.AccountId!.Value)
        };
    }
}
