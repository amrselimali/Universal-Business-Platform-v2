using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class PosRepository
{
    /// <summary>صرف المخزون للصنف المباشر (مستند ISSUE مُرحّل) وإرجاع تكلفته الفعلية.</summary>
    private static async Task<(Guid? LineId, decimal Cost)> IssueStockAsync(
        NpgsqlConnection connection, NpgsqlTransaction tx, PosCheckoutInput input,
        DirectLine line, string docNo, Guid userId, bool allowNegative, CancellationToken ct)
    {
        var available = await connection.ExecuteScalarAsync<decimal>(new CommandDefinition(
            "SELECT COALESCE(fn_stock_available(@itemId, @warehouseId), 0)",
            new { itemId = line.ItemId, warehouseId = input.WarehouseId }, tx,
            cancellationToken: ct));
        if (available < line.Quantity)
            throw new ValidationAppException("validation.booking_not_available");

        var documentId = Guid.CreateVersion7();
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO stock_documents
                (id, doc_type, doc_date, branch_id, warehouse_id, status, notes, created_by)
            VALUES (@id, 'ISSUE', @date, @branchId, @warehouseId, 'DRAFT', @notes, @userId)
            """, new
        {
            id = documentId, date = input.DocDate.Date, branchId = input.BranchId,
            warehouseId = input.WarehouseId, notes = $"تنفيذ مباشر {docNo}", userId
        }, tx, cancellationToken: ct));

        var lineId = Guid.CreateVersion7();
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO stock_document_lines
                (id, document_id, line_no, direction, item_id, unit_id, conversion_factor,
                 quantity, base_quantity, unit_cost)
            VALUES (@id, @documentId, 1, 'OUT', @itemId, @unitId, 1, @quantity, @quantity, 0)
            """, new
        {
            id = lineId, documentId, itemId = line.ItemId, unitId = line.UnitId,
            quantity = line.Quantity
        }, tx, cancellationToken: ct));

        await StockPostingRepository.PostCoreAsync(connection, tx, documentId, userId,
            allowNegative, ct);

        var cost = await connection.ExecuteScalarAsync<decimal>(new CommandDefinition(
            "SELECT COALESCE(total_cost, 0) FROM stock_document_lines WHERE id = @id",
            new { id = lineId }, tx, cancellationToken: ct));
        return (lineId, cost);
    }

    /// <summary>قيد التكلفة: من حساب تكلفة المخزن (تصنيف الصنف) إلى حساب المخزن.</summary>
    private static async Task<Guid> PostCostAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, Guid? categoryId, Guid branchId, string docNo, decimal amount,
        Guid executionId, Guid userId, CancellationToken ct)
    {
        var accounts = await connection.QuerySingleOrDefaultAsync<(Guid? Cogs, Guid? Inventory)>(
            new CommandDefinition("""
            SELECT cogs_account_id AS Cogs, inventory_account_id AS Inventory
            FROM item_category_accounts WHERE category_id = @categoryId
            """, new { categoryId }, tx, cancellationToken: ct));
        var cogs = accounts.Cogs ?? await NeedAsync(connection, tx, PostingKeys.StockCogs, ct);
        var inventory = accounts.Inventory ??
                        await NeedAsync(connection, tx, PostingKeys.StockInventory, ct);

        return await PostEntryAsync(connection, tx, "stock", branchId, DateTime.UtcNow.Date,
            docNo, $"تكلفة تنفيذ مباشر {docNo}", new List<EntryLine>
            {
                new(cogs, amount, 0), new(inventory, 0, amount)
            }, userId, "execution", executionId, ct);
    }

    /// <summary>قيد الإيراد: مدين العميل بالإجمالي، دائن الإيراد بالصافي وضريبة المخرجات.</summary>
    private static async Task<Guid?> PostRevenueAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, PosCheckoutInput input, DirectLine line, string docNo,
        Guid executionId, Guid userId, CancellationToken ct)
    {
        if (line.TotalAmount <= 0) return null;
        var receivable = await NeedAsync(connection, tx, PostingKeys.CustomerReceivable, ct);
        var revenue = await NeedAsync(connection, tx, PostingKeys.SalesRevenue, ct);
        var net = line.NetAmount > 0 ? line.NetAmount : line.TotalAmount;
        var entries = new List<EntryLine> { new(receivable, line.TotalAmount, 0, input.CustomerId) };
        entries.Add(new EntryLine(revenue, 0, net));
        if (line.TaxAmount > 0)
            entries.Add(new EntryLine(
                await NeedAsync(connection, tx, PostingKeys.VatOutput, ct), 0, line.TaxAmount));

        return await PostEntryAsync(connection, tx, "sales", input.BranchId, input.DocDate.Date,
            docNo, $"إيراد تنفيذ مباشر {docNo}", entries, userId, "execution", executionId, ct);
    }
}