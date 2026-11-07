using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class PosRepository
{
    /// <summary>
    /// يحدد مخزنًا فيه رصيد كافٍ للصنف: المخزن المختار أولًا، ثم أي مخزن آخر في نفس الفرع.
    /// يمنع رسالة «لا توجد كمية» عندما يكون الرصيد مستلَمًا في مخزن آخر.
    /// </summary>
    private static async Task<(Guid WarehouseId, decimal Available)> ResolveWarehouseAsync(
        NpgsqlConnection connection, NpgsqlTransaction tx, PosCheckoutInput input,
        DirectLine line, CancellationToken ct)
    {
        var selected = await connection.ExecuteScalarAsync<decimal>(new CommandDefinition(
            "SELECT COALESCE(fn_stock_available(@itemId, @warehouseId), 0)",
            new { itemId = line.ItemId, warehouseId = input.WarehouseId }, tx,
            cancellationToken: ct));
        if (selected >= line.Quantity)
            return (input.WarehouseId, selected);

        var fallback = await connection.QuerySingleOrDefaultAsync<(Guid WarehouseId,
            decimal Available)>(new CommandDefinition("""
            SELECT b.warehouse_id AS WarehouseId,
                   COALESCE(b.on_hand - b.reserved_qty, 0) AS Available
            FROM stock_balances b
            JOIN warehouses w ON w.id = b.warehouse_id
            JOIN warehouse_branches wb ON wb.warehouse_id = w.id
            WHERE b.item_id = @itemId AND wb.branch_id = @branchId AND w.is_active
              AND COALESCE(b.on_hand - b.reserved_qty, 0) >= @quantity
            ORDER BY (b.warehouse_id = @selected) DESC, (b.on_hand - b.reserved_qty) DESC
            LIMIT 1
            """, new { itemId = line.ItemId, branchId = input.BranchId,
                quantity = line.Quantity, selected = input.WarehouseId }, tx,
            cancellationToken: ct));

        if (fallback.WarehouseId != Guid.Empty) return fallback;
        return (input.WarehouseId, selected);
    }

    /// <summary>صرف المخزون للصنف المباشر (مستند ISSUE مُرحّل) وإرجاع تكلفته والمخزن المصروف منه.</summary>
    private static async Task<(Guid? LineId, decimal Cost, Guid WarehouseId)> IssueStockAsync(
        NpgsqlConnection connection, NpgsqlTransaction tx, PosCheckoutInput input,
        DirectLine line, string docNo, Guid userId, bool allowNegative, CancellationToken ct)
    {
        var (warehouseId, available) = await ResolveWarehouseAsync(connection, tx, input, line, ct);
        if (!allowNegative && available < line.Quantity)
            throw new ValidationAppException("validation.stock_not_available_detail",
                new Dictionary<string, string>
                {
                    ["item"] = line.ItemId.ToString("N")[..8],
                    ["requested"] = line.Quantity.ToString("N4"),
                    ["available"] = available.ToString("N4")
                });

        var documentId = Guid.CreateVersion7();
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO stock_documents
                (id, doc_type, doc_date, branch_id, warehouse_id, status, notes, created_by)
            VALUES (@id, 'ISSUE', @date, @branchId, @warehouseId, 'DRAFT', @notes, @userId)
            """, new
        {
            id = documentId, date = input.DocDate.Date, branchId = input.BranchId,
            warehouseId, notes = $"تنفيذ مباشر {docNo}", userId
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
        return (lineId, cost, warehouseId);
    }

    /// <summary>قيد التكلفة: من حساب تكلفة المخزن (تصنيف الصنف) إلى حساب المخزن.</summary>
    private static async Task<Guid> PostCostAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, Guid? categoryId, Guid branchId, DateTime docDate, string docNo,
        decimal amount, Guid executionId, Guid userId, CancellationToken ct)
    {
        var accounts = await connection.QuerySingleOrDefaultAsync<(Guid? Cogs, Guid? Inventory)>(
            new CommandDefinition("""
            SELECT cogs_account_id AS Cogs, inventory_account_id AS Inventory
            FROM item_category_accounts WHERE category_id = @categoryId
            """, new { categoryId }, tx, cancellationToken: ct));
        var cogs = accounts.Cogs ?? await NeedAsync(connection, tx, PostingKeys.StockCogs, ct);
        var inventory = accounts.Inventory ??
                        await NeedAsync(connection, tx, PostingKeys.StockInventory, ct);

        return await PostEntryAsync(connection, tx, "stock", branchId, docDate.Date,
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
        // الجلسة الهدية: الكمية تُنفَّذ بلا قيمة ولا قيد إيراد — 0084.
        if (line.OperationType == ServiceOperationTypes.Gift) return null;
        if (line.TotalAmount <= 0) return null;
        var receivable = await NeedAsync(connection, tx, PostingKeys.CustomerReceivable, ct);
        var revenue = await NeedAsync(connection, tx, PostingKeys.SalesRevenue, ct);
        var net = line.NetAmount > 0 ? line.NetAmount : line.TotalAmount;
        var entries = new List<EntryLine> { new(receivable, line.TotalAmount, 0, input.CustomerId) };

        // الجلسة العلاجية تُرحَّل دائمًا علاجية (حساب الموظف مدين / العميل دائن) — 0084.
        if (line.OperationType == ServiceOperationTypes.Treatment)
        {
            var treatmentAccount = line.TreatmentAccountId;
            if (treatmentAccount is null || treatmentAccount == Guid.Empty)
                throw new ValidationAppException("validation.treatment_account_missing");
            entries.Add(new EntryLine(treatmentAccount.Value, net, 0));
            entries.Add(new EntryLine(receivable, 0, net, input.CustomerId));
        }

        entries.Add(new EntryLine(revenue, 0, net));
        if (line.TaxAmount > 0)
            entries.Add(new EntryLine(
                await NeedAsync(connection, tx, PostingKeys.VatOutput, ct), 0, line.TaxAmount));

        return await PostEntryAsync(connection, tx, "sales", input.BranchId, input.DocDate.Date,
            docNo, $"إيراد تنفيذ مباشر {docNo}", entries, userId, "execution", executionId, ct);
    }
}