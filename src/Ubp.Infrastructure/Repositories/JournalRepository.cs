using Dapper;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IJournalRepository
{
    Task<PagedResult<Journal>> ListAsync(JournalFilter filter, PageRequest request,
        CancellationToken ct = default);
    Task<IReadOnlyList<Journal>> ListOptionsAsync(bool includeInactive, CancellationToken ct = default);
    Task<Journal?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Journal?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<Journal?> GetByTypeAsync(string journalType, CancellationToken ct = default);
    Task<Guid> CreateAsync(Guid id, JournalInput input, Guid userId, CancellationToken ct = default);
    Task<Journal?> UpdateAsync(Guid id, int version, JournalInput input, Guid userId,
        CancellationToken ct = default);
    Task<Journal?> SetActiveAsync(Guid id, bool isActive, Guid userId, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed partial class JournalRepository : IJournalRepository
{
    private const string Columns = """
        j.id AS Id, j.code AS Code, j.name_ar AS NameAr, j.name_en AS NameEn,
        j.journal_type AS JournalType, j.default_account_id AS DefaultAccountId,
        a.name_ar AS DefaultAccountName, j.allow_manual AS AllowManual, j.is_system AS IsSystem,
        j.sort_order AS SortOrder, j.is_active AS IsActive, j.version AS Version,
        (SELECT count(*)::int FROM journal_entries e WHERE e.journal_id = j.id) AS EntriesCount
        """;

    private const string FromSql = """
        FROM journals j LEFT JOIN accounts a ON a.id = j.default_account_id
        """;

    private static readonly IReadOnlyDictionary<string, string> SortMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["code"] = "j.code", ["name_ar"] = "j.name_ar", ["sort_order"] = "j.sort_order",
            ["journal_type"] = "j.journal_type"
        };

    private readonly IDbConnectionFactory _factory;

    public JournalRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<PagedResult<Journal>> ListAsync(JournalFilter filter, PageRequest request,
        CancellationToken ct = default)
    {
        var where = new List<string>();
        if (!filter.IncludeInactive) where.Add("j.is_active");
        if (!string.IsNullOrWhiteSpace(filter.JournalType)) where.Add("j.journal_type = @journalType");
        if (!string.IsNullOrWhiteSpace(request.Search))
            where.Add("(j.code ILIKE '%' || @search || '%' OR normalize_search(j.name_ar) " +
                      "LIKE '%' || normalize_search(@search) || '%')");

        var whereSql = where.Count == 0 ? "WHERE true" : "WHERE " + string.Join(" AND ", where);
        var order = SqlSort.Build(request.SortBy, SortMap, "j.sort_order", request.Descending);
        var sql = $"""
            SELECT {Columns} {FromSql} {whereSql}
            ORDER BY {order} LIMIT @take OFFSET @offset;
            SELECT count(*) FROM journals j {whereSql};
            """;

        await using var connection = await _factory.OpenAsync(ct);
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, new
        {
            search = request.Search, take = request.SafePageSize, offset = request.Offset,
            journalType = filter.JournalType
        }, cancellationToken: ct));
        var items = (await multi.ReadAsync<Journal>()).ToList();
        var total = await multi.ReadFirstAsync<int>();
        return new PagedResult<Journal>
        {
            Items = items, Total = total, Page = request.SafePage, PageSize = request.SafePageSize
        };
    }

    public async Task<IReadOnlyList<Journal>> ListOptionsAsync(bool includeInactive,
        CancellationToken ct = default)
    {
        var where = includeInactive ? "WHERE true" : "WHERE j.is_active";
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<Journal>(new CommandDefinition(
            $"SELECT {Columns} {FromSql} {where} ORDER BY j.sort_order, j.code", cancellationToken: ct));
        return rows.ToList();
    }
}
