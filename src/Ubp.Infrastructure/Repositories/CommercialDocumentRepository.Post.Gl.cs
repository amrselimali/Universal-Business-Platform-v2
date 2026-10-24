using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class CommercialDocumentRepository
{
    /// <summary>قيد الأستاذ: فاتورة (مدين مخزون + ضريبة / دائن مورد) ومرتجع (عكس + فرق السعر).</summary>
    private static async Task CreateEntryAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        PostContext context, Guid userId, CancellationToken ct)
    {
        Guid Need(string key)
        {
            if (context.Defaults.TryGetValue(key, out var account)) return account;
            throw new ValidationAppException("validation.posting_account_missing",
                new Dictionary<string, string> { ["key"] = key, ["module"] = "purchasing" });
        }

        var payable = Need(PostingKeys.SupplierPayable);
        var vat = context.TaxTotal > 0 ? Need(PostingKeys.VatInput) : (Guid?)null;
        var gl = new List<(Guid Account, decimal Debit, decimal Credit, Guid? Supplier)>();

        void Add(Guid account, decimal debit, decimal credit, bool isParty = false)
        {
            if (debit != 0 || credit != 0)
                gl.Add((account, debit, credit, isParty ? context.SupplierId : null));
        }

        if (!context.IsReturn)
        {
            foreach (var line in context.Lines)
                Add(context.Inventory[line.LineNo], line.NetAmount, 0);
            if (vat is not null) Add(vat.Value, context.TaxTotal, 0);
            Add(payable, 0, context.Total, true);
        }
        else
        {
            Add(payable, context.Total, 0, true);
            foreach (var line in context.Lines)
            {
                var cost = Math.Round(context.Costs.TryGetValue(line.LineNo, out var c) ? c : 0m, 2);
                Add(context.Inventory[line.LineNo], 0, cost);
            }
            if (vat is not null) Add(vat.Value, 0, context.TaxTotal);
        }

        var difference = Math.Round(gl.Sum(x => x.Debit) - gl.Sum(x => x.Credit), 2,
            MidpointRounding.AwayFromZero);
        if (difference != 0)
        {
            var balancing = context.Defaults.TryGetValue(PostingKeys.PurchaseVariance, out var variance)
                ? variance
                : Need(PostingKeys.RoundingDifference);
            Add(balancing, difference > 0 ? difference : 0, difference < 0 ? -difference : 0);
        }

        var journalId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT id FROM journals WHERE journal_type = 'purchase' AND is_active " +
            "ORDER BY sort_order LIMIT 1", transaction: tx, cancellationToken: ct))
            ?? throw new ValidationAppException("validation.journal_not_found");

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO journal_entries (id, journal_id, branch_id, entry_date, reference,
                description, status, source_type, total_debit, total_credit, created_by)
            VALUES (@entryId, @journalId, @branchId, @docDate, @docNo, @description, 'draft',
                    'invoice', @debit, @credit, @userId)
            """, new
        {
            context.EntryId, journalId, branchId = context.BranchId, docDate = context.DocDate,
            context.DocNo,
            description = $"قيد {(context.IsReturn ? "مرتجع شراء" : "فاتورة شراء")} {context.DocNo}",
            debit = gl.Sum(x => x.Debit), credit = gl.Sum(x => x.Credit), userId
        }, tx, cancellationToken: ct));

        var lineNo = 0;
        foreach (var item in gl)
        {
            lineNo++;
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO journal_entry_lines (id, entry_id, line_no, account_id, description,
                    debit, credit, supplier_id, branch_id, entry_date)
                VALUES (@lineId, @entryId, @lineNo, @account, @description, @debit, @credit,
                        @supplierId, @branchId, @docDate)
                """, new
            {
                lineId = Guid.CreateVersion7(), entryId = context.EntryId, lineNo,
                account = item.Account, description = $"مستند {context.DocNo}",
                debit = item.Debit, credit = item.Credit, supplierId = item.Supplier,
                branchId = context.BranchId,
                docDate = context.DocDate
            }, tx, cancellationToken: ct));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT fn_gl_post_entry(@entryId, @userId)",
            new { entryId = context.EntryId, userId }, tx, cancellationToken: ct));
    }
}
