using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IEmployeeAdvanceRepository
{
    Task<EmployeeAdvance> CreateAndPostAsync(EmployeeAdvanceInput input, Guid userId,
        CancellationToken ct = default);
    Task<EmployeeAdvance?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<EmployeeAdvance>> ListAsync(Guid branchId, DateTime? from, DateTime? to,
        int limit = 200, CancellationToken ct = default);
}

/// <summary>
/// صرف سلفة لموظف من المصروفات التشغيلية: مدين الحساب المُختار (تحميل السلفة) / دائن الخزينة
/// أو حساب المقاصة لطريقة الدفع، مع ربط الموظف لكشف حسابه — 0084.
/// </summary>
public sealed class EmployeeAdvanceRepository : IEmployeeAdvanceRepository
{
    private readonly IDbConnectionFactory _factory;
    public EmployeeAdvanceRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<EmployeeAdvance> CreateAndPostAsync(EmployeeAdvanceInput input, Guid userId,
        CancellationToken ct = default)
    {
        if (input.BranchId == Guid.Empty || input.EmployeeId == Guid.Empty ||
            input.TreasuryId == Guid.Empty)
            throw new ValidationAppException("validation.required_fields");
        if (input.Amount <= 0)
            throw new ValidationAppException("validation.advance_amount");

        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var treasury = await connection.QuerySingleOrDefaultAsync<(Guid AccountId, Guid BranchId,
            Guid? ClearingAccountId)>(new CommandDefinition("""
            SELECT t.account_id AS AccountId, t.branch_id AS BranchId,
                   m.clearing_account_id AS ClearingAccountId
            FROM treasuries t
            LEFT JOIN treasury_methods m ON m.id = @methodId AND m.treasury_id = t.id
            WHERE t.id = @treasuryId AND t.is_active
            """, new { treasuryId = input.TreasuryId, methodId = input.TreasuryMethodId }, tx,
            cancellationToken: ct));
        if (treasury.AccountId == Guid.Empty)
            throw new ValidationAppException("validation.treasury_not_found");
        if (treasury.BranchId != input.BranchId)
            throw new ValidationAppException("validation.treasury_branch_mismatch");

        var chargeAccount = input.ChargeAccountId is { } c && c != Guid.Empty ? c : (Guid?)null;
        if (chargeAccount is null)
        {
            chargeAccount = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                "SELECT account_id FROM posting_defaults WHERE setting_key = @key",
                new { key = PostingKeys.EmployeeAdvances }, tx, cancellationToken: ct));
            if (chargeAccount is null || chargeAccount == Guid.Empty)
                throw new ValidationAppException("validation.posting_account_missing",
                    new Dictionary<string, string>
                    {
                        ["key"] = PostingKeys.EmployeeAdvances, ["module"] = "expenses"
                    });
        }

        var docNo = await connection.ExecuteScalarAsync<string>(new CommandDefinition("""
            SELECT next_document_number('EMPLOYEE_ADVANCE'::varchar, @branchId::uuid,
                                        @date::date, 'ADV'::varchar, 6::smallint)
            """, new { branchId = input.BranchId, date = input.DocDate.Date }, tx,
            cancellationToken: ct));

        var id = Guid.CreateVersion7();
        var creditAccount = treasury.ClearingAccountId ?? treasury.AccountId;
        var entryId = Guid.CreateVersion7();
        var journalId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT id FROM journals WHERE journal_type = 'cash' AND is_active " +
            "ORDER BY sort_order LIMIT 1", transaction: tx, cancellationToken: ct))
            ?? throw new ValidationAppException("validation.journal_not_found");

        var description = $"سلفة موظف {docNo}";
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO journal_entries
                (id, journal_id, branch_id, entry_date, reference, description, status,
                 source_type, source_id, total_debit, total_credit, created_by)
            VALUES (@entryId, @journalId, @branchId, @date, @docNo, @description, 'draft',
                    'employee_advance', @id, @amount, @amount, @userId)
            """, new { entryId, journalId, branchId = input.BranchId, date = input.DocDate.Date,
                docNo, description, id, amount = input.Amount, userId }, tx, cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO journal_entry_lines
                (id, entry_id, line_no, account_id, description, debit, credit, branch_id, entry_date)
            VALUES (@id1, @entryId, 1, @charge, @description, @amount, 0, @branchId, @date),
                   (@id2, @entryId, 2, @credit, @description, 0, @amount, @branchId, @date)
            """, new { id1 = Guid.CreateVersion7(), id2 = Guid.CreateVersion7(), entryId,
                charge = chargeAccount.Value, credit = creditAccount, description,
                amount = input.Amount, branchId = input.BranchId, date = input.DocDate.Date }, tx,
            cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT fn_gl_post_entry(@entryId, @userId)", new { entryId, userId }, tx,
            cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO employee_advances
                (id, branch_id, doc_no, doc_date, employee_id, treasury_id, treasury_method_id,
                 charge_account_id, amount, status, journal_entry_id, notes, created_by,
                 posted_at, posted_by)
            VALUES (@id, @branchId, @docNo, @date, @employeeId, @treasuryId, @methodId,
                    @charge, @amount, 'posted', @entryId, @notes, @userId, now(), @userId)
            """, new { id, branchId = input.BranchId, docNo, date = input.DocDate.Date,
                employeeId = input.EmployeeId, treasuryId = input.TreasuryId,
                methodId = input.TreasuryMethodId, charge = chargeAccount.Value,
                amount = input.Amount, entryId, notes = input.Notes, userId }, tx,
            cancellationToken: ct));

        await tx.CommitAsync(ct);
        return (await GetAsync(id, ct))!;
    }

    public async Task<EmployeeAdvance?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<EmployeeAdvance>(new CommandDefinition("""
            SELECT a.id AS Id, a.branch_id AS BranchId, b.name_ar AS BranchName, a.doc_no AS DocNo,
                   a.doc_date AS DocDate, a.employee_id AS EmployeeId, e.name_ar AS EmployeeName,
                   a.treasury_id AS TreasuryId, t.name_ar AS TreasuryName,
                   a.treasury_method_id AS TreasuryMethodId, m.name_ar AS MethodName,
                   a.charge_account_id AS ChargeAccountId, acc.name_ar AS ChargeAccountName,
                   a.amount AS Amount, a.status AS Status, a.journal_entry_id AS JournalEntryId,
                   a.notes AS Notes, a.created_at AS CreatedAt, a.posted_at AS PostedAt
            FROM employee_advances a
            JOIN branches b ON b.id = a.branch_id
            JOIN employees e ON e.id = a.employee_id
            JOIN treasuries t ON t.id = a.treasury_id
            LEFT JOIN treasury_methods m ON m.id = a.treasury_method_id
            JOIN accounts acc ON acc.id = a.charge_account_id
            WHERE a.id = @id
            """, new { id }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<EmployeeAdvance>> ListAsync(Guid branchId, DateTime? from,
        DateTime? to, int limit = 200, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return (await connection.QueryAsync<EmployeeAdvance>(new CommandDefinition("""
            SELECT a.id AS Id, a.branch_id AS BranchId, a.doc_no AS DocNo, a.doc_date AS DocDate,
                   a.employee_id AS EmployeeId, e.name_ar AS EmployeeName, a.treasury_id AS TreasuryId,
                   t.name_ar AS TreasuryName, a.charge_account_id AS ChargeAccountId,
                   acc.name_ar AS ChargeAccountName, a.amount AS Amount, a.status AS Status,
                   a.notes AS Notes, a.created_at AS CreatedAt
            FROM employee_advances a
            JOIN employees e ON e.id = a.employee_id
            JOIN treasuries t ON t.id = a.treasury_id
            JOIN accounts acc ON acc.id = a.charge_account_id
            WHERE a.branch_id = @branchId
              AND (@from::date IS NULL OR a.doc_date >= @from)
              AND (@to::date IS NULL OR a.doc_date <= @to)
            ORDER BY a.created_at DESC
            LIMIT @limit
            """, new { branchId, from, to, limit }, cancellationToken: ct))).ToList();
    }
}
