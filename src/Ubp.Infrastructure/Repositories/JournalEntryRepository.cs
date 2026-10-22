using Dapper;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IJournalEntryRepository
{
    Task<PagedResult<JournalEntry>> ListAsync(EntryFilter filter, PageRequest request,
        CancellationToken ct = default);
    Task<IReadOnlyList<JournalEntry>> ListBySourceAsync(string sourceType, Guid branchId,
        CancellationToken ct = default);
    Task<JournalEntry?> GetAsync(Guid id, CancellationToken ct = default);
    Task<JournalEntry?> GetBySourceAsync(string sourceType, Guid sourceId,
        CancellationToken ct = default);
    Task<JournalEntryDetail?> GetDetailAsync(Guid id, CancellationToken ct = default);
    Task<Guid> CreateDraftAsync(Guid id, JournalEntryInput input, Guid userId,
        string sourceType = EntrySources.Manual, Guid? sourceId = null,
        CancellationToken ct = default);
    Task<JournalEntry?> UpdateDraftAsync(Guid id, int version, JournalEntryInput input, Guid userId,
        CancellationToken ct = default);
    Task<JournalEntry?> CancelAsync(Guid id, Guid userId, CancellationToken ct = default);
    Task<bool> DeleteDraftAsync(Guid id, CancellationToken ct = default);
}

public sealed partial class JournalEntryRepository : IJournalEntryRepository
{
    private const string HeaderColumns = """
        e.id AS Id, e.journal_id AS JournalId, j.code AS JournalCode, j.name_ar AS JournalName,
        e.branch_id AS BranchId, b.name_ar AS BranchName, e.entry_no AS EntryNo,
        e.entry_date AS EntryDate, e.reference AS Reference, e.description AS Description,
        e.status AS Status, e.source_type AS SourceType, e.source_id AS SourceId,
        e.reversal_of_id AS ReversalOfId, e.is_reversed AS IsReversed,
        e.total_debit AS TotalDebit, e.total_credit AS TotalCredit,
        e.posted_at AS PostedAt, u.name AS PostedByName,
        e.created_at AS CreatedAt, e.updated_at AS UpdatedAt, e.version AS Version,
        (SELECT count(*)::int FROM journal_entry_lines l WHERE l.entry_id = e.id) AS LinesCount
        """;

    private const string HeaderFrom = """
        FROM journal_entries e
        JOIN journals j ON j.id = e.journal_id
        JOIN branches b ON b.id = e.branch_id
        LEFT JOIN users u ON u.id = e.posted_by
        """;

    private const string LineColumns = """
        l.id AS Id, l.entry_id AS EntryId, l.line_no AS LineNo, l.account_id AS AccountId,
        a.code AS AccountCode, a.name_ar AS AccountName, a.account_type AS AccountType,
        a.cost_center_policy AS CostCenterPolicy, l.description AS Description,
        l.debit AS Debit, l.credit AS Credit, l.customer_id AS CustomerId,
        cu.name_ar AS CustomerName, l.supplier_id AS SupplierId, su.name_ar AS SupplierName,
        l.cost_center_id AS CostCenterId, cc.name_ar AS CostCenterName,
        l.branch_id AS BranchId, l.entry_date AS EntryDate, l.is_posted AS IsPosted
        """;

    private const string LineFrom = """
        FROM journal_entry_lines l
        JOIN accounts a ON a.id = l.account_id
        LEFT JOIN customers cu ON cu.id = l.customer_id
        LEFT JOIN suppliers su ON su.id = l.supplier_id
        LEFT JOIN cost_centers cc ON cc.id = l.cost_center_id
        """;

    private static readonly IReadOnlyDictionary<string, string> SortMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["entry_no"] = "e.entry_no", ["entry_date"] = "e.entry_date", ["status"] = "e.status",
            ["total_debit"] = "e.total_debit", ["created_at"] = "e.created_at"
        };

    private readonly IDbConnectionFactory _factory;

    public JournalEntryRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<PagedResult<JournalEntry>> ListAsync(EntryFilter filter, PageRequest request,
        CancellationToken ct = default)
    {
        var where = new List<string>();
        if (!string.IsNullOrWhiteSpace(filter.Status)) where.Add("e.status = @status");
        if (!string.IsNullOrWhiteSpace(filter.SourceType)) where.Add("e.source_type = @sourceType");
        if (filter.JournalId.HasValue) where.Add("e.journal_id = @journalId");
        if (filter.BranchId.HasValue) where.Add("e.branch_id = @branchId");
        if (filter.From.HasValue) where.Add("e.entry_date >= @from");
        if (filter.To.HasValue) where.Add("e.entry_date <= @to");
        if (!string.IsNullOrWhiteSpace(request.Search))
            where.Add("(e.entry_no ILIKE '%' || @search || '%' OR e.reference ILIKE '%' || @search || '%' " +
                      "OR normalize_search(e.description) LIKE '%' || normalize_search(@search) || '%')");

        var whereSql = where.Count == 0 ? "WHERE true" : "WHERE " + string.Join(" AND ", where);
        var order = SqlSort.Build(request.SortBy, SortMap, "e.entry_date", request.Descending);
        var sql = $"""
            SELECT {HeaderColumns} {HeaderFrom} {whereSql}
            ORDER BY {order} LIMIT @take OFFSET @offset;
            SELECT count(*) FROM journal_entries e {whereSql};
            """;

        await using var connection = await _factory.OpenAsync(ct);
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, new
        {
            search = request.Search, take = request.SafePageSize, offset = request.Offset,
            status = filter.Status, sourceType = filter.SourceType, journalId = filter.JournalId,
            branchId = filter.BranchId, from = filter.From, to = filter.To
        }, cancellationToken: ct));
        var items = (await multi.ReadAsync<JournalEntry>()).ToList();
        var total = await multi.ReadFirstAsync<int>();
        return new PagedResult<JournalEntry>
        {
            Items = items, Total = total, Page = request.SafePage, PageSize = request.SafePageSize
        };
    }
}
