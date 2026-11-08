using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class OperatingExpenseRepository
{
    private sealed class PostLine
    {
        public Guid Id { get; set; }
        public int LineNo { get; set; }
        public Guid AccountId { get; set; }
        public Guid? ItemId { get; set; }
        public Guid? UnitId { get; set; }
        public Guid? WarehouseId { get; set; }
        public Guid? CostCenterId { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal Amount { get; set; }
    }

    private sealed class PostHeader
    {
        public Guid Id { get; set; }
        public Guid BranchId { get; set; }
        public DateTime DocDate { get; set; }
        public Guid SupplierId { get; set; }
        public Guid? TreasuryId { get; set; }
        public Guid? TreasuryMethodId { get; set; }
        public decimal TotalAmount { get; set; }
        public string Status { get; set; } = "";
        public string? Notes { get; set; }
    }

    public async Task<OperatingExpense> PostAsync(Guid id, Guid userId, bool allowNegative,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var head = await connection.QuerySingleOrDefaultAsync<PostHeader>(new CommandDefinition("""
            SELECT id AS Id, branch_id AS BranchId, doc_date AS DocDate, supplier_id AS SupplierId,
                   treasury_id AS TreasuryId, treasury_method_id AS TreasuryMethodId,
                   total_amount AS TotalAmount, status AS Status, notes AS Notes
            FROM operating_expenses WHERE id = @id FOR UPDATE
            """, new { id }, tx, cancellationToken: ct)) ?? throw new NotFoundAppException();

        if (head.Status != OperatingExpenseStatuses.Draft)
            throw new ValidationAppException("validation.expense_not_draft");

        var lines = (await connection.QueryAsync<PostLine>(new CommandDefinition("""
            SELECT id AS Id, line_no AS LineNo, account_id AS AccountId, item_id AS ItemId,
                   unit_id AS UnitId, warehouse_id AS WarehouseId,
                   cost_center_id AS CostCenterId, quantity AS Quantity,
                   unit_price AS UnitPrice, amount AS Amount
            FROM operating_expense_lines WHERE expense_id = @id ORDER BY line_no
            """, new { id }, tx, cancellationToken: ct))).ToList();
        if (lines.Count == 0)
            throw new ValidationAppException("validation.document_has_no_lines");

        var docNo = await connection.ExecuteScalarAsync<string>(new CommandDefinition("""
            SELECT next_document_number('EXPENSE'::varchar, @branchId::uuid, @date::date,
                                        'EXP'::varchar, 6::smallint)
            """, new { branchId = head.BranchId, date = head.DocDate.Date }, tx,
            cancellationToken: ct));

        var stockDocumentId = await PostStockLinesAsync(connection, tx, head, lines, docNo,
            userId, ct);

        var total = Math.Round(lines.Sum(l => l.Amount), 2, MidpointRounding.AwayFromZero);
        var entryId = await PostExpenseEntryAsync(connection, tx, head, lines, total, docNo,
            userId, ct);

        Guid? voucherId = null;
        if (head.TreasuryId is not null)
            voucherId = await PostPaymentAsync(connection, tx, head, total, docNo, userId, ct);

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE operating_expenses
            SET status = 'POSTED', doc_no = @docNo, posted_at = now(), posted_by = @userId,
                journal_entry_id = @entryId, stock_document_id = @stockDocumentId,
                voucher_id = @voucherId, updated_by = @userId
            WHERE id = @id
            """, new { docNo, userId, entryId, stockDocumentId, voucherId, id }, tx,
            cancellationToken: ct));

        await tx.CommitAsync(ct);
        return (await GetAsync(id, ct))!;
    }
}
