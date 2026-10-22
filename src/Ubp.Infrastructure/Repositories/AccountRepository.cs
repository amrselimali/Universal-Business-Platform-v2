using Dapper;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IAccountRepository
{
    Task<PagedResult<Account>> ListAsync(AccountFilter filter, PageRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<Account>> ListAllAsync(bool includeInactive, CancellationToken ct = default);
    Task<IReadOnlyList<Account>> ListPostableAsync(CancellationToken ct = default);
    Task<Account?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Account?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<Account?> FindByTypeAsync(string accountType, CancellationToken ct = default);
    Task<int> CountUsageAsync(Guid id, CancellationToken ct = default);
    Task<Guid> CreateAsync(Guid id, AccountInput input, string path, short level, Guid userId,
        bool isSystem = false, CancellationToken ct = default);
    Task<Account?> UpdateAsync(Guid id, int version, AccountInput input, Guid userId,
        CancellationToken ct = default);
    Task<Account?> SetActiveAsync(Guid id, bool isActive, Guid userId, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<Account?> MoveAsync(Guid id, Guid? newParentId, Guid userId, CancellationToken ct = default);
    Task<int> CountAsync(CancellationToken ct = default);
}

public sealed partial class AccountRepository : IAccountRepository
{
    private const string Columns = """
        a.id AS Id, a.code AS Code, a.name_ar AS NameAr, a.name_en AS NameEn,
        a.parent_id AS ParentId, a.path AS Path, a.level AS Level, a.is_group AS IsGroup,
        a.account_type AS AccountType, a.cost_center_policy AS CostCenterPolicy,
        a.is_system AS IsSystem, a.notes AS Notes, a.is_active AS IsActive,
        a.version AS Version, a.created_at AS CreatedAt, a.updated_at AS UpdatedAt,
        (SELECT count(*)::int FROM accounts c WHERE c.parent_id = a.id) AS ChildrenCount,
        (SELECT count(*)::int FROM journal_entry_lines l WHERE l.account_id = a.id) AS LinesCount
        """;

    private static readonly IReadOnlyDictionary<string, string> SortMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["code"] = "a.code", ["name_ar"] = "a.name_ar", ["path"] = "a.path",
            ["level"] = "a.level", ["account_type"] = "a.account_type"
        };

    private readonly IDbConnectionFactory _factory;

    public AccountRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<PagedResult<Account>> ListAsync(AccountFilter filter, PageRequest request,
        CancellationToken ct = default)
    {
        var where = new List<string>();
        if (!filter.IncludeInactive) where.Add("a.is_active");
        if (filter.IsGroup.HasValue) where.Add("a.is_group = @isGroup");
        if (!string.IsNullOrWhiteSpace(filter.AccountType)) where.Add("a.account_type = @accountType");
        if (filter.PostableOnly) where.Add("NOT a.is_group");
        if (filter.ParentId.HasValue) where.Add("a.parent_id = @parentId");
        if (!string.IsNullOrWhiteSpace(request.Search)) where.Add(SearchSql.Contains("a.search_text"));

        var whereSql = where.Count == 0 ? "WHERE true" : "WHERE " + string.Join(" AND ", where);
        var order = SqlSort.Build(request.SortBy, SortMap, "a.path", request.Descending);
        var sql = $"""
            SELECT {Columns} FROM accounts a {whereSql}
            ORDER BY {order} LIMIT @take OFFSET @offset;
            SELECT count(*) FROM accounts a {whereSql};
            """;

        await using var connection = await _factory.OpenAsync(ct);
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, new
        {
            search = request.Search, take = request.SafePageSize, offset = request.Offset,
            isGroup = filter.IsGroup, accountType = filter.AccountType, parentId = filter.ParentId
        }, cancellationToken: ct));
        var items = (await multi.ReadAsync<Account>()).ToList();
        var total = await multi.ReadFirstAsync<int>();
        return new PagedResult<Account>
        {
            Items = items, Total = total, Page = request.SafePage, PageSize = request.SafePageSize
        };
    }
}
