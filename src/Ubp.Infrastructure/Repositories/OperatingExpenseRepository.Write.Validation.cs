using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class OperatingExpenseRepository
{
    /// <summary>
    /// تعبئة الحسابات الافتراضية قبل التحقق: الحساب الذي يُرحَّل عليه المصروف يُحدَّد افتراضياً
    /// من شاشة إعدادات الحسابات (لا يُطلب في الشاشة)، وطريقة الدفع الداخلية للخزينة تُستنتج
    /// تلقائياً — 0093.
    /// </summary>
    private static async Task ResolveDefaultsAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, OperatingExpenseInput input, CancellationToken ct)
    {
        if (input.TreasuryId == Guid.Empty) input.TreasuryId = null;
        if (input.TreasuryMethodId == Guid.Empty) input.TreasuryMethodId = null;
        if (input.TreasuryId is { } treasuryId && input.TreasuryMethodId is null)
            input.TreasuryMethodId = await DefaultMethodIdAsync(connection, tx, treasuryId, ct);

        Guid? defaultAccount = null;
        foreach (var line in input.Lines.Where(l => l.AccountId == Guid.Empty))
        {
            // سطر مخزني: يُحمَّل على حساب مخزون تصنيف الصنف (أو حساب المخزون الافتراضي).
            if (line.ItemId is { } itemId)
            {
                var stockAccount = await StockAccountAsync(connection, tx, itemId, ct);
                if (stockAccount is { } accountId && accountId != Guid.Empty)
                {
                    line.AccountId = accountId;
                    continue;
                }
            }

            // سطر مصروف: يُحدَّد الحساب افتراضياً من شاشة إعدادات الحسابات — 0093.
            defaultAccount ??= await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                "SELECT account_id FROM posting_defaults WHERE setting_key = @key",
                new { key = PostingKeys.OperatingExpense }, tx, cancellationToken: ct));
            if (defaultAccount is null || defaultAccount == Guid.Empty)
                throw new ValidationAppException("validation.posting_account_missing",
                    new Dictionary<string, string>
                    {
                        ["key"] = PostingKeys.OperatingExpense, ["module"] = "expenses"
                    });
            line.AccountId = defaultAccount.Value;
        }
    }

    /// <summary>حساب تحميل سطر مخزني: حساب مخزون التصنيف ثم حساب المخزون الافتراضي — 0093.</summary>
    private static async Task<Guid?> StockAccountAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, Guid itemId, CancellationToken ct) =>
        await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
            SELECT COALESCE(
                (SELECT ca.inventory_account_id FROM items i
                 JOIN item_category_accounts ca ON ca.category_id = i.category_id
                 WHERE i.id = @itemId AND ca.is_active),
                (SELECT account_id FROM posting_defaults WHERE setting_key = @key))
            """, new { itemId, key = PostingKeys.StockInventory }, tx, cancellationToken: ct));

    /// <summary>معرّف الطريقة الافتراضية للخزينة (أو أول طريقة نشطة لها) — 0093.</summary>
    internal static async Task<Guid?> DefaultMethodIdAsync(NpgsqlConnection connection,
        NpgsqlTransaction? tx, Guid treasuryId, CancellationToken ct) =>
        await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
            SELECT id FROM treasury_methods
            WHERE treasury_id = @treasuryId AND is_active
            ORDER BY (code = 'DEFAULT') DESC, code
            LIMIT 1
            """, new { treasuryId }, tx, cancellationToken: ct));

    /// <summary>تحقق بنيوي قبل الحفظ: المورد، طريقة الدفع، الحسابات، والبنود المخزنية.</summary>
    private static async Task ValidateAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        OperatingExpenseInput input, CancellationToken ct)
    {
        if (input.BranchId == Guid.Empty || input.SupplierId == Guid.Empty)
            throw new ValidationAppException("validation.required_fields");
        if (input.DocDate.Date > DateTime.Today.AddDays(1))
            throw new ValidationAppException("validation.expense_date_invalid");
        if (input.Lines.Count == 0)
            throw new ValidationAppException("validation.document_has_no_lines");
        if (input.TreasuryId is null && input.TreasuryMethodId is not null)
            throw new ValidationAppException("validation.expense_payment_incomplete");

        var supplier = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*)::int FROM suppliers WHERE id = @id AND is_active",
            new { id = input.SupplierId }, tx, cancellationToken: ct));
        if (supplier == 0)
            throw new ValidationAppException("validation.supplier_not_found");

        if (input.TreasuryMethodId is { } methodId)
        {
            var method = await connection.QuerySingleOrDefaultAsync<(Guid BranchId, bool Ok)>(
                new CommandDefinition("""
                    SELECT t.branch_id AS BranchId, true AS Ok
                    FROM treasury_methods m JOIN treasuries t ON t.id = m.treasury_id
                    WHERE m.id = @methodId AND m.treasury_id = @treasuryId
                      AND m.is_active AND t.is_active
                    """, new { methodId, treasuryId = input.TreasuryId }, tx,
                    cancellationToken: ct));
            if (!method.Ok)
                throw new ValidationAppException("validation.treasury_method_not_found");
            if (method.BranchId != input.BranchId)
                throw new ValidationAppException("validation.treasury_branch_mismatch");
        }

        foreach (var line in input.Lines)
            await ValidateLineAsync(connection, tx, input.BranchId, line, ct);
    }

    private static async Task ValidateLineAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        Guid branchId, OperatingExpenseLineInput line, CancellationToken ct)
    {
        if (line.AccountId == Guid.Empty)
            throw new ValidationAppException("validation.expense_account_required");
        if (line.Quantity <= 0 || line.UnitPrice < 0)
            throw new ValidationAppException("validation.expense_line_invalid");
        if (line.Amount <= 0)
            throw new ValidationAppException("validation.expense_amount_required");

        var account = await connection.QuerySingleOrDefaultAsync<(bool IsGroup, bool IsActive)>(
            new CommandDefinition(
                "SELECT is_group AS IsGroup, is_active AS IsActive FROM accounts WHERE id = @id",
                new { id = line.AccountId }, tx, cancellationToken: ct));
        if (!account.IsActive || account.IsGroup)
            throw new ValidationAppException("validation.expense_account_not_postable");

        var costCenterMismatch = await connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
            SELECT CASE WHEN a.cost_center_policy = 2 AND @costCenterId IS NULL THEN true
                        WHEN a.cost_center_policy = 0 AND @costCenterId IS NOT NULL THEN true
                        ELSE false END
            FROM accounts a WHERE a.id = @accountId
            """, new { accountId = line.AccountId, costCenterId = line.CostCenterId }, tx,
            cancellationToken: ct));
        if (costCenterMismatch)
            throw new ValidationAppException("validation.expense_cost_center_policy");

        if (line.ItemId is not { } itemId) return;

        var item = await connection.QuerySingleOrDefaultAsync<(string ItemType, bool IsActive)>(
            new CommandDefinition(
                "SELECT item_type AS ItemType, is_active AS IsActive FROM items WHERE id = @id",
                new { id = itemId }, tx, cancellationToken: ct));
        if (!item.IsActive || item.ItemType != "GOODS")
            throw new ValidationAppException("validation.expense_item_not_stockable");
        if (line.UnitId is null || line.WarehouseId is null)
            throw new ValidationAppException("validation.expense_stock_line_incomplete");

        var linked = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT count(*)::int FROM warehouse_branches
            WHERE warehouse_id = @warehouseId AND branch_id = @branchId
            """, new { warehouseId = line.WarehouseId, branchId }, tx, cancellationToken: ct));
        if (linked == 0)
            throw new ValidationAppException("validation.warehouse_branch_mismatch");
    }
}
