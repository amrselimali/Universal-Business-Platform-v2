using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class BookingExecutionRepository
{
    private static async Task<Guid> PostCogsAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, Guid? categoryId, Guid branchId, string docNo, decimal amount,
        Guid executionId, Guid userId, CancellationToken ct)
    {
        var defaults = (await connection.QueryAsync<(string Key, Guid? AccountId)>(
            new CommandDefinition("SELECT setting_key, account_id FROM posting_defaults",
                transaction: tx, cancellationToken: ct)))
            .Where(r => r.AccountId.HasValue)
            .ToDictionary(r => r.Key, r => r.AccountId!.Value, StringComparer.OrdinalIgnoreCase);
        Guid Need(string key) => defaults.TryGetValue(key, out var account) ? account
            : throw new ValidationAppException("validation.posting_account_missing",
                new Dictionary<string, string> { ["key"] = key, ["module"] = "sales" });

        // حسابات تصنيف الصنف أولاً (تكلفة المخزن/المخزن)، ثم الحسابات العامة.
        var categoryAccounts = await connection.QuerySingleOrDefaultAsync<(Guid? Cogs,
            Guid? Inventory)>(new CommandDefinition("""
            SELECT cogs_account_id AS Cogs, inventory_account_id AS Inventory
            FROM item_category_accounts WHERE category_id = @categoryId
            """, new { categoryId }, tx, cancellationToken: ct));
        var cogs = categoryAccounts.Cogs ?? Need(PostingKeys.StockCogs);
        var inventory = categoryAccounts.Inventory ?? Need(PostingKeys.StockInventory);

        var journalId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT id FROM journals WHERE journal_type = 'stock' AND is_active " +
            "ORDER BY sort_order LIMIT 1", transaction: tx, cancellationToken: ct))
            ?? throw new ValidationAppException("validation.journal_not_found");

        var entryId = Guid.CreateVersion7();
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO journal_entries
                (id, journal_id, branch_id, entry_date, reference, description, status, source_type,
                 source_id, total_debit, total_credit, created_by)
            VALUES (@entryId, @journalId, @branchId, @date, @docNo, @description, 'draft',
                    'execution', @executionId, @amount, @amount, @userId)
            """, new
        {
            entryId, journalId, branchId, date = DateTime.UtcNow.Date, docNo,
            description = $"تكلفة تنفيذ {docNo}", executionId, amount, userId
        }, tx, cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO journal_entry_lines
                (id, entry_id, line_no, account_id, description, debit, credit, branch_id,
                 entry_date)
            VALUES (@id, @entryId, 1, @account, @description, @amount, 0, @branchId, @date),
                   (@id2, @entryId, 2, @account2, @description, 0, @amount, @branchId, @date)
            """, new
        {
            id = Guid.CreateVersion7(), id2 = Guid.CreateVersion7(), entryId,
            account = cogs, account2 = inventory, description = $"تكلفة تنفيذ {docNo}",
            amount, branchId, date = DateTime.UtcNow.Date
        }, tx, cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT fn_gl_post_entry(@entryId, @userId)", new { entryId, userId }, tx,
            cancellationToken: ct));
        return entryId;
    }

    /// <summary>
    /// إيراد الخدمة البيعية عند التنفيذ: مدين حساب العميل بقيمة المنفذ، ودائن حساب الإيراد
    /// (صافي) وضريبة المخرجات إن وجدت؛ القيمة تُحتسب بنسبة الكمية المنفذة من سطر الحجز.
    /// </summary>
    private static async Task<Guid?> PostServiceRevenueAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, ExecutionLine line, decimal executedQty, string docNo,
        Guid executionLineId, Guid executionId, Guid userId, CancellationToken ct)
    {
        if (line.Quantity <= 0) return null;
        var ratio = executedQty / line.Quantity;
        var net = Math.Round(line.NetAmount * ratio, 2, MidpointRounding.AwayFromZero);
        var total = Math.Round(line.TotalAmount * ratio, 2, MidpointRounding.AwayFromZero);
        if (total <= 0) return null;
        // ضمان اتزان القيد: الإيراد بين صفر والإجمالي، والفرق ضريبة مخرجات.
        if (net <= 0 || net > total) net = total;
        var tax = Math.Round(total - net, 2, MidpointRounding.AwayFromZero);

        var defaults = (await connection.QueryAsync<(string Key, Guid? AccountId)>(
            new CommandDefinition("SELECT setting_key, account_id FROM posting_defaults",
                transaction: tx, cancellationToken: ct)))
            .Where(r => r.AccountId.HasValue)
            .ToDictionary(r => r.Key, r => r.AccountId!.Value, StringComparer.OrdinalIgnoreCase);
        Guid Need(string key) => defaults.TryGetValue(key, out var account) ? account
            : throw new ValidationAppException("validation.posting_account_missing",
                new Dictionary<string, string> { ["key"] = key, ["module"] = "sales" });

        var receivable = Need(PostingKeys.CustomerReceivable);
        var revenue = Need(PostingKeys.SalesRevenue);
        var vat = tax > 0 ? Need(PostingKeys.VatOutput) : (Guid?)null;

        var journalId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT id FROM journals WHERE journal_type = 'sales' AND is_active " +
            "ORDER BY sort_order LIMIT 1", transaction: tx, cancellationToken: ct))
            ?? throw new ValidationAppException("validation.journal_not_found");

        var entryId = Guid.CreateVersion7();
        var description = $"إيراد تنفيذ خدمة {docNo}";
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO journal_entries
                (id, journal_id, branch_id, entry_date, reference, description, status, source_type,
                 source_id, total_debit, total_credit, created_by)
            VALUES (@entryId, @journalId, @branchId, @date, @docNo, @description, 'draft',
                    'execution', @executionLineId, @total, @total, @userId)
            """, new
        {
            entryId, journalId, branchId = line.BranchId, date = DateTime.UtcNow.Date, docNo,
            description, executionLineId, total, userId
        }, tx, cancellationToken: ct));

        var gl = new List<(Guid AccountId, decimal Debit, decimal Credit, Guid? CustomerId)>
        {
            (receivable, total, 0, line.CustomerId)
        };
        if (net > 0) gl.Add((revenue, 0, net, null));
        if (vat is { } vatAccount && tax > 0) gl.Add((vatAccount, 0, tax, null));

        var lineNo = 0;
        foreach (var item in gl)
        {
            lineNo++;
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO journal_entry_lines
                    (id, entry_id, line_no, account_id, description, debit, credit, customer_id,
                     branch_id, entry_date)
                VALUES (@id, @entryId, @lineNo, @accountId, @description, @debit, @credit,
                        @customerId, @branchId, @date)
                """, new
            {
                id = Guid.CreateVersion7(), entryId, lineNo, accountId = item.AccountId,
                description, debit = item.Debit, credit = item.Credit,
                customerId = item.CustomerId, branchId = line.BranchId,
                date = DateTime.UtcNow.Date
            }, tx, cancellationToken: ct));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT fn_gl_post_entry(@entryId, @userId)", new { entryId, userId }, tx,
            cancellationToken: ct));
        return entryId;
    }
}
