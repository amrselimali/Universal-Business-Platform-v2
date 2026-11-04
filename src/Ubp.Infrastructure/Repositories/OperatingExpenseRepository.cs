using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IOperatingExpenseRepository
{
    Task<IReadOnlyList<OperatingExpense>> ListAsync(Guid? branchId,
        IReadOnlyCollection<Guid> branchIds, Guid? supplierId, DateTime? from, DateTime? to,
        string? status, int limit, CancellationToken ct = default);
    Task<OperatingExpense?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Guid> CreateAsync(Guid id, OperatingExpenseInput input, Guid userId,
        CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<OperatingExpense> PostAsync(Guid id, Guid userId, bool allowNegative,
        CancellationToken ct = default);
}

/// <summary>
/// مستند المصروفات التشغيلية: سطور حرّة على شجرة الحسابات (+ بنود مخزنية اختيارية)،
/// يُرحَّل على حساب المورد (آجل) أو يُسدد فورًا من خزينة/طريقة دفع بسند صرف داخل نفس المعاملة.
/// </summary>
public sealed partial class OperatingExpenseRepository : IOperatingExpenseRepository
{
    private readonly IDbConnectionFactory _factory;
    public OperatingExpenseRepository(IDbConnectionFactory factory) => _factory = factory;

    private const string HeaderColumns = """
        e.id AS Id, e.branch_id AS BranchId, b.name_ar AS BranchName, e.doc_no AS DocNo,
        e.doc_date AS DocDate, e.supplier_id AS SupplierId, s.name_ar AS SupplierName,
        e.treasury_id AS TreasuryId, t.name_ar AS TreasuryName,
        e.treasury_method_id AS TreasuryMethodId, m.name_ar AS MethodName,
        e.notes AS Notes, e.total_amount AS TotalAmount, e.status AS Status,
        e.journal_entry_id AS JournalEntryId, e.stock_document_id AS StockDocumentId,
        e.voucher_id AS VoucherId, e.created_at AS CreatedAt, e.posted_at AS PostedAt
        """;

    private const string HeaderJoins = """
        FROM operating_expenses e
        JOIN branches b ON b.id = e.branch_id
        JOIN suppliers s ON s.id = e.supplier_id
        LEFT JOIN treasuries t ON t.id = e.treasury_id
        LEFT JOIN treasury_methods m ON m.id = e.treasury_method_id
        """;

    public async Task<IReadOnlyList<OperatingExpense>> ListAsync(Guid? branchId,
        IReadOnlyCollection<Guid> branchIds, Guid? supplierId, DateTime? from, DateTime? to,
        string? status, int limit, CancellationToken ct = default)
    {
        var take = limit is < 1 or > 500 ? 200 : limit;
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<OperatingExpense>(new CommandDefinition($"""
            SELECT {HeaderColumns} {HeaderJoins}
            WHERE (@allBranches OR e.branch_id = ANY(@branchIds))
              AND (@branchId IS NULL OR e.branch_id = @branchId)
              AND (@supplierId IS NULL OR e.supplier_id = @supplierId)
              AND (@from IS NULL OR e.doc_date >= @from::date)
              AND (@to IS NULL OR e.doc_date <= @to::date)
              AND (@status IS NULL OR e.status = @status)
            ORDER BY e.doc_date DESC, e.created_at DESC
            LIMIT @take
            """, new
        {
            allBranches = branchIds.Count == 0, branchIds = branchIds.ToArray(), branchId,
            supplierId, from = from?.Date, to = to?.Date, status, take
        }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<OperatingExpense?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var expense = await connection.QuerySingleOrDefaultAsync<OperatingExpense>(
            new CommandDefinition($"SELECT {HeaderColumns} {HeaderJoins} WHERE e.id = @id",
                new { id }, cancellationToken: ct));
        if (expense is null) return null;
        expense.Lines = await LoadLinesAsync(connection, id, ct);
        return expense;
    }

    private static async Task<IReadOnlyList<OperatingExpenseLine>> LoadLinesAsync(
        NpgsqlConnection connection, Guid expenseId, CancellationToken ct)
    {
        var rows = await connection.QueryAsync<OperatingExpenseLine>(new CommandDefinition("""
            SELECT l.id AS Id, l.line_no AS LineNo, l.account_id AS AccountId,
                   a.code AS AccountCode, a.name_ar AS AccountName,
                   l.item_id AS ItemId, i.code AS ItemCode, i.name_ar AS ItemName,
                   l.unit_id AS UnitId, l.warehouse_id AS WarehouseId,
                   w.name_ar AS WarehouseName, l.cost_center_id AS CostCenterId,
                   c.name_ar AS CostCenterName, l.description AS Description,
                   l.quantity AS Quantity, l.unit_price AS UnitPrice, l.amount AS Amount,
                   l.stock_document_id AS StockDocumentId
            FROM operating_expense_lines l
            JOIN accounts a ON a.id = l.account_id
            LEFT JOIN items i ON i.id = l.item_id
            LEFT JOIN warehouses w ON w.id = l.warehouse_id
            LEFT JOIN cost_centers c ON c.id = l.cost_center_id
            WHERE l.expense_id = @expenseId
            ORDER BY l.line_no
            """, new { expenseId }, cancellationToken: ct));
        return rows.ToList();
    }
}
