using Dapper;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class AccountingSettingsRepository
{
    public async Task<IReadOnlyList<ItemCategoryAccount>> ListCategoryAccountsAsync(
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<ItemCategoryAccount>(new CommandDefinition("""
            SELECT m.id AS Id, m.category_id AS CategoryId, c.code AS CategoryCode,
                   c.name_ar AS CategoryName, m.inventory_account_id AS InventoryAccountId,
                   m.cogs_account_id AS CogsAccountId,
                   m.adjustment_gain_account_id AS AdjustmentGainAccountId,
                   m.adjustment_loss_account_id AS AdjustmentLossAccountId,
                   ia.name_ar AS InventoryAccountName, ca.name_ar AS CogsAccountName,
                   m.version AS Version
            FROM item_category_accounts m
            JOIN item_categories c ON c.id = m.category_id
            JOIN accounts ia ON ia.id = m.inventory_account_id
            JOIN accounts ca ON ca.id = m.cogs_account_id
            ORDER BY c.code
            """, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task UpsertCategoryAccountAsync(Guid id, ItemCategoryAccountInput input,
        Guid userId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO item_category_accounts
                (id, category_id, inventory_account_id, cogs_account_id,
                 adjustment_gain_account_id, adjustment_loss_account_id, created_by)
            VALUES (@id, @CategoryId, @InventoryAccountId, @CogsAccountId,
                    @AdjustmentGainAccountId, @AdjustmentLossAccountId, @userId)
            ON CONFLICT (category_id) DO UPDATE SET
                inventory_account_id = EXCLUDED.inventory_account_id,
                cogs_account_id = EXCLUDED.cogs_account_id,
                adjustment_gain_account_id = EXCLUDED.adjustment_gain_account_id,
                adjustment_loss_account_id = EXCLUDED.adjustment_loss_account_id,
                is_active = true,
                updated_by = @userId
            """, new
        {
            id, input.CategoryId, input.InventoryAccountId, input.CogsAccountId,
            input.AdjustmentGainAccountId, input.AdjustmentLossAccountId, userId
        }, cancellationToken: ct));
    }

    public async Task<bool> DeleteCategoryAccountAsync(Guid categoryId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM item_category_accounts WHERE category_id = @categoryId",
            new { categoryId }, cancellationToken: ct)) > 0;
    }

    public async Task<int> CountCategoriesWithoutAccountsAsync(CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT count(*)::int FROM item_categories c
            WHERE c.is_active
              AND NOT EXISTS (SELECT 1 FROM item_category_accounts m WHERE m.category_id = c.id)
            """, cancellationToken: ct));
    }
}
