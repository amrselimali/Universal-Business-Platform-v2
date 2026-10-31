using Dapper;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class AssetRepository
{
    public async Task<IReadOnlyList<AssetCategory>> ListCategoriesAsync(
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<AssetCategory>(new CommandDefinition("""
            SELECT id AS Id, code AS Code, name_ar AS NameAr,
                   depreciation_method AS DepreciationMethod,
                   depreciation_frequency AS DepreciationFrequency,
                   useful_life_months AS UsefulLifeMonths, is_metered AS IsMetered,
                   asset_account_id AS AssetAccountId, accum_account_id AS AccumAccountId,
                   expense_account_id AS ExpenseAccountId,
                   maintenance_expense_account_id AS MaintenanceExpenseAccountId,
                   is_active AS IsActive, expected_units AS ExpectedUnits
            FROM asset_categories WHERE is_active ORDER BY code
            """, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<Guid> CreateCategoryAsync(AssetCategoryInput input, Guid userId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition("""
            INSERT INTO asset_categories
                (code, name_ar, depreciation_method, depreciation_frequency, useful_life_months,
                 is_metered, asset_account_id, accum_account_id, expense_account_id,
                 maintenance_expense_account_id, expected_units)
            VALUES (@Code, @NameAr, @DepreciationMethod, @DepreciationFrequency, @UsefulLifeMonths,
                    @IsMetered, @AssetAccountId, @AccumAccountId, @ExpenseAccountId,
                    @MaintenanceExpenseAccountId, @ExpectedUnits)
            RETURNING id
            """, new
        {
            input.Code, input.NameAr, input.DepreciationMethod, input.DepreciationFrequency,
            input.UsefulLifeMonths, input.IsMetered, input.AssetAccountId, input.AccumAccountId,
            input.ExpenseAccountId, input.MaintenanceExpenseAccountId, input.ExpectedUnits
        }, cancellationToken: ct));
    }
}
