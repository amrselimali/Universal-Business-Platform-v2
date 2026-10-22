using Dapper;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IStockAccountingRepository
{
    Task<IReadOnlyList<StockPostingCandidate>> ListCandidatesAsync(Guid? branchId, DateTime from,
        DateTime to, CancellationToken ct = default);
    Task<StockPostingPreview> PreviewAsync(Guid documentId, CancellationToken ct = default);
    Task<Guid> GenerateAsync(Guid documentId, Guid userId, CancellationToken ct = default);
    Task<int> GenerateAllAsync(Guid? branchId, DateTime from, DateTime to, Guid userId,
        CancellationToken ct = default);
}

public sealed partial class StockAccountingRepository : IStockAccountingRepository
{
    private readonly IDbConnectionFactory _factory;

    public StockAccountingRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<StockPostingCandidate>> ListCandidatesAsync(Guid? branchId,
        DateTime from, DateTime to, CancellationToken ct = default)
    {
        const string sql = """
            SELECT d.id AS DocumentId, d.doc_no AS DocNo, d.doc_type AS DocType,
                   d.doc_date AS DocDate, d.branch_id AS BranchId, b.name_ar AS BranchName,
                   d.total_cost AS TotalCost, e.id AS EntryId, e.entry_no AS EntryNo,
                   EXISTS (SELECT 1 FROM stock_document_lines l
                           JOIN items i ON i.id = l.item_id
                           LEFT JOIN item_category_accounts m ON m.category_id = i.category_id
                           WHERE l.document_id = d.id
                             AND m.id IS NULL) AS IsMissingAccounts
            FROM stock_documents d
            JOIN branches b ON b.id = d.branch_id
            LEFT JOIN journal_entries e
                   ON e.source_type = 'stock_document' AND e.source_id = d.id
                  AND e.reversal_of_id IS NULL AND e.status <> 'cancelled'
            WHERE d.status IN ('POSTED', 'PARTIALLY_RECEIVED', 'RECEIVED')
              AND d.doc_type <> 'TRANSFER'
              AND d.doc_date BETWEEN @from::date AND @to::date
              AND (@branchId::uuid IS NULL OR d.branch_id = @branchId::uuid)
            ORDER BY d.doc_date DESC, d.doc_no DESC NULLS LAST
            LIMIT 500
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<StockPostingCandidate>(new CommandDefinition(sql,
            new { branchId, from, to }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<StockPostingPreview> PreviewAsync(Guid documentId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var header = await connection.QuerySingleOrDefaultAsync<DocHead>(new CommandDefinition("""
            SELECT d.id AS Id, d.doc_no AS DocNo, d.doc_type AS DocType, d.doc_date AS DocDate,
                   d.branch_id AS BranchId, b.name_ar AS BranchName, d.total_cost AS TotalCost,
                   d.status AS Status
            FROM stock_documents d JOIN branches b ON b.id = d.branch_id
            WHERE d.id = @documentId
            """, new { documentId }, cancellationToken: ct));

        var preview = new StockPostingPreview();
        if (header is null)
        {
            preview.ErrorKey = "validation.document_not_found";
            return preview;
        }

        preview.Document = new StockPostingCandidate
        {
            DocumentId = header.Id, DocNo = header.DocNo, DocType = header.DocType,
            DocDate = header.DocDate, BranchId = header.BranchId, BranchName = header.BranchName,
            TotalCost = header.TotalCost
        };

        if (header.Status is not ("POSTED" or "PARTIALLY_RECEIVED" or "RECEIVED"))
        {
            preview.ErrorKey = "validation.document_not_posted";
            return preview;
        }

        var build = await BuildLinesAsync(connection, documentId, ct);
        preview.Lines = build.Lines;
        preview.ErrorKey = build.ErrorKey;
        preview.Document.IsMissingAccounts = build.MissingCategoryAccounts;
        return preview;
    }

    public async Task<Guid> GenerateAsync(Guid documentId, Guid userId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT pg_advisory_xact_lock(hashtext('stock-journal:' || @documentId::text))",
            new { documentId }, tx, cancellationToken: ct));

        var existing = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
            SELECT id FROM journal_entries
            WHERE source_type = 'stock_document' AND source_id = @documentId
              AND reversal_of_id IS NULL AND status <> 'cancelled'
            """, new { documentId }, tx, cancellationToken: ct));
        if (existing.HasValue) return existing.Value;

        var entryId = await GenerateCoreAsync(connection, tx, documentId, userId, ct);
        await tx.CommitAsync(ct);
        return entryId;
    }

    public async Task<int> GenerateAllAsync(Guid? branchId, DateTime from, DateTime to, Guid userId,
        CancellationToken ct = default)
    {
        var candidates = await ListCandidatesAsync(branchId, from, to, ct);
        var created = 0;
        foreach (var candidate in candidates.Where(c => c.EntryId is null))
        {
            try
            {
                await GenerateAsync(candidate.DocumentId, userId, ct);
                created++;
            }
            catch (AppException)
            {
                // مستند بلا حسابات مرحّلة أو مستند مُرحّل بالفعل — يُتخطى ويكمل الباقي
            }
        }
        return created;
    }
}
