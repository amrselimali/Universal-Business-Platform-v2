using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class StockAccountingRepository
{
    /// <summary>إنشاء قيد المخزون وترحيله داخل نفس معاملة استدعاء التوليد.</summary>
    private static async Task<Guid> GenerateCoreAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        Guid documentId, Guid userId, CancellationToken ct)
    {
        var head = await connection.QuerySingleOrDefaultAsync<DocHead>(new CommandDefinition("""
            SELECT d.id AS Id, d.doc_no AS DocNo, d.doc_type AS DocType, d.doc_date AS DocDate,
                   d.branch_id AS BranchId, b.name_ar AS BranchName, d.total_cost AS TotalCost,
                   d.status AS Status
            FROM stock_documents d JOIN branches b ON b.id = d.branch_id
            WHERE d.id = @documentId
            """, new { documentId }, tx, cancellationToken: ct));

        if (head is null) throw new NotFoundAppException("validation.document_not_found");
        if (head.Status is not ("POSTED" or "PARTIALLY_RECEIVED" or "RECEIVED"))
            throw new ValidationAppException("validation.document_not_posted");

        var build = await BuildLinesAsync(connection, documentId, ct, tx);
        if (build.ErrorKey is not null) throw new ValidationAppException(build.ErrorKey);

        var journalId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT id FROM journals WHERE journal_type = 'stock' AND is_active " +
            "ORDER BY sort_order LIMIT 1", transaction: tx, cancellationToken: ct));
        if (journalId is null) throw new ValidationAppException("validation.stock_journal_missing");

        var entryId = Guid.CreateVersion7();
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO journal_entries
                (id, journal_id, branch_id, entry_date, reference, description, status,
                 source_type, source_id, created_by)
            VALUES (@entryId, @journalId, @BranchId, @DocDate, @DocNo, @description, 'draft',
                    'stock_document', @documentId, @userId)
            """, new
        {
            entryId, journalId, head.BranchId, head.DocDate, head.DocNo,
            description = $"قيد مستند مخزون {head.DocNo}", documentId, userId
        }, tx, cancellationToken: ct));

        var lineNo = 0;
        foreach (var line in build.Lines)
        {
            lineNo++;
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO journal_entry_lines
                    (id, entry_id, line_no, account_id, description, debit, credit,
                     branch_id, entry_date)
                VALUES (@id, @entryId, @lineNo, @AccountId, @description, @Debit, @Credit,
                        @branchId, @entryDate)
                """, new
            {
                id = Guid.CreateVersion7(), entryId, lineNo, line.AccountId,
                description = $"مستند {head.DocNo}", line.Debit, line.Credit,
                branchId = head.BranchId, entryDate = head.DocDate
            }, tx, cancellationToken: ct));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT fn_gl_post_entry(@entryId, @userId)", new { entryId, userId }, tx,
            cancellationToken: ct));

        return entryId;
    }
}
