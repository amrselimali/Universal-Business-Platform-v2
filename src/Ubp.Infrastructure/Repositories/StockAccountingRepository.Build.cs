using Dapper;
using Npgsql;
using Ubp.Core.Models;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class StockAccountingRepository
{
    /// <summary>
    /// يبني بنود القيد من سطور المستند: المخزون من تصنيف الصنف، والطرف المقابل من
    /// posting_defaults. يُراعي نوع المستند الأصلي عند العكس، ويجمع البنود المتشابهة.
    /// </summary>
    private static async Task<BuildResult> BuildLinesAsync(NpgsqlConnection connection,
        Guid documentId, CancellationToken ct, NpgsqlTransaction? tx = null)
    {
        var defaults = (await connection.QueryAsync<(string Key, Guid? AccountId)>(
            new CommandDefinition("SELECT setting_key, account_id FROM posting_defaults",
                transaction: tx, cancellationToken: ct)))
            .Where(r => r.AccountId.HasValue)
            .ToDictionary(r => r.Key, r => r.AccountId!.Value);

        var lines = (await connection.QueryAsync<StockLineRow>(new CommandDefinition("""
            SELECT l.direction AS Direction, l.total_cost AS TotalCost,
                   coalesce(orig.doc_type, d.doc_type) AS EffectiveType,
                   i.category_id AS CategoryId,
                   m.inventory_account_id AS InventoryAccountId,
                   m.cogs_account_id AS CogsAccountId,
                   m.adjustment_gain_account_id AS AdjustmentGainAccountId,
                   m.adjustment_loss_account_id AS AdjustmentLossAccountId
            FROM stock_document_lines l
            JOIN stock_documents d ON d.id = l.document_id
            JOIN items i ON i.id = l.item_id
            LEFT JOIN item_category_accounts m ON m.category_id = i.category_id
            LEFT JOIN stock_documents orig ON orig.id = d.reversal_of_id
            WHERE l.document_id = @documentId
            ORDER BY l.line_no
            """, new { documentId }, tx, cancellationToken: ct))).ToList();

        var result = new BuildResult();
        var amounts = new Dictionary<Guid, (decimal Debit, decimal Credit)>();

        foreach (var line in lines)
        {
            var amount = Math.Round(line.TotalCost, 2, MidpointRounding.AwayFromZero);
            if (amount <= 0) continue;
            Accumulate(line, amount, defaults, amounts, result);
        }

        if (result.MissingCategoryAccounts)
        {
            result.ErrorKey = "validation.stock_accounts_missing";
            return result;
        }

        var debit = amounts.Sum(a => a.Value.Debit);
        var credit = amounts.Sum(a => a.Value.Credit);
        var difference = Math.Round(debit - credit, 2, MidpointRounding.AwayFromZero);

        result.Lines = amounts
            .Select(a => new JournalEntryLineInput
            {
                AccountId = a.Key, Debit = a.Value.Debit, Credit = a.Value.Credit
            })
            .Where(l => l.Debit != 0 || l.Credit != 0)
            .ToList();

        if (difference != 0) ApplyRounding(result, defaults, difference);
        if (result.Lines.Count == 0) result.ErrorKey = "validation.document_has_no_lines";
        return result;
    }
}
