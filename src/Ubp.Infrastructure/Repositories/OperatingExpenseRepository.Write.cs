using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class OperatingExpenseRepository
{
    public async Task<Guid> CreateAsync(Guid id, OperatingExpenseInput input, Guid userId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        await ValidateAsync(connection, tx, input, ct);

        var total = input.Lines.Sum(l => l.Amount);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO operating_expenses
                (id, branch_id, doc_date, supplier_id, treasury_id, treasury_method_id, notes,
                 total_amount, status, created_by)
            VALUES (@id, @branchId, @docDate, @supplierId, @treasuryId, @treasuryMethodId, @notes,
                    @total, 'DRAFT', @userId)
            """, new
        {
            id, branchId = input.BranchId, docDate = input.DocDate.Date, input.SupplierId,
            input.TreasuryId, input.TreasuryMethodId, Notes = Clean(input.Notes), total, userId
        }, tx, cancellationToken: ct));

        var lineNo = 0;
        foreach (var line in input.Lines)
        {
            lineNo++;
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO operating_expense_lines
                    (expense_id, line_no, account_id, item_id, unit_id, warehouse_id,
                     cost_center_id, description, quantity, unit_price, amount)
                VALUES (@expenseId, @lineNo, @accountId, @itemId, @unitId, @warehouseId,
                        @costCenterId, @description, @quantity, @unitPrice, @amount)
                """, new
            {
                expenseId = id, lineNo, accountId = line.AccountId, itemId = line.ItemId,
                unitId = line.UnitId, warehouseId = line.WarehouseId,
                costCenterId = line.CostCenterId, description = Clean(line.Description),
                quantity = line.Quantity, unitPrice = line.UnitPrice, amount = line.Amount
            }, tx, cancellationToken: ct));
        }

        await tx.CommitAsync(ct);
        return id;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM operating_expenses WHERE id = @id AND status = 'DRAFT'",
            new { id }, cancellationToken: ct)) > 0;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
