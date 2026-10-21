using Dapper;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IItemCategoryRepository
{
    Task<PagedResult<ItemCategory>> ListAsync(PageRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<ItemCategory>> ListOptionsAsync(CancellationToken ct = default);
    Task<ItemCategory?> GetAsync(Guid id, CancellationToken ct = default);
    Task<ItemCategory> CreateAsync(ItemCategoryInput input, CancellationToken ct = default);
    Task<ItemCategory?> UpdateAsync(Guid id, int version, ItemCategoryInput input, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<ItemCategory?> SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default);
    Task<int> CountItemsAsync(Guid id, CancellationToken ct = default);
    Task<int> CountChildrenAsync(Guid id, CancellationToken ct = default);
}

public sealed class ItemCategoryRepository : IItemCategoryRepository
{
    private const string Columns = """
        c.id AS Id, c.code AS Code, c.name_ar AS NameAr, c.name_en AS NameEn,
        c.parent_id AS ParentId,
        (SELECT p.name_ar FROM item_categories p WHERE p.id = c.parent_id) AS ParentName,
        (SELECT count(*)::int FROM items i WHERE i.category_id = c.id) AS ItemCount,
        c.is_active AS IsActive, c.created_at AS CreatedAt, c.updated_at AS UpdatedAt, c.version AS Version
        """;

    private static readonly IReadOnlyDictionary<string, string> SortMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["code"] = "c.code", ["name_ar"] = "c.name_ar"
        };

    private readonly IDbConnectionFactory _factory;

    public ItemCategoryRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<PagedResult<ItemCategory>> ListAsync(PageRequest request, CancellationToken ct = default)
    {
        var where = new List<string>();
        if (!request.IncludeInactive) where.Add("c.is_active");
        if (!string.IsNullOrWhiteSpace(request.Search))
            where.Add("c.search_text LIKE '%' || normalize_search(@search) || '%'");

        var whereSql = where.Count == 0 ? "WHERE true" : "WHERE " + string.Join(" AND ", where);
        var order = SqlSort.Build(request.SortBy, SortMap, "c.code", request.Descending);
        var sql = $"""
            SELECT {Columns} FROM item_categories c {whereSql}
            ORDER BY {order} LIMIT @take OFFSET @offset;
            SELECT count(*) FROM item_categories c {whereSql};
            """;

        await using var connection = await _factory.OpenAsync(ct);
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, new
        {
            search = request.Search, take = request.SafePageSize, offset = request.Offset
        }, cancellationToken: ct));
        var items = (await multi.ReadAsync<ItemCategory>()).ToList();
        var total = await multi.ReadFirstAsync<int>();
        return new PagedResult<ItemCategory>
        {
            Items = items, Total = total, Page = request.SafePage, PageSize = request.SafePageSize
        };
    }

    public async Task<IReadOnlyList<ItemCategory>> ListOptionsAsync(CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<ItemCategory>(new CommandDefinition(
            "SELECT id AS Id, code AS Code, name_ar AS NameAr, name_en AS NameEn FROM item_categories WHERE is_active ORDER BY name_ar",
            cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<ItemCategory?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<ItemCategory>(new CommandDefinition(
            $"SELECT {Columns} FROM item_categories c WHERE c.id = @id", new { id }, cancellationToken: ct));
    }

    public async Task<ItemCategory> CreateAsync(ItemCategoryInput input, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO item_categories (code, name_ar, name_en, parent_id, is_active)
            VALUES (@Code, @NameAr, @NameEn, @ParentId, @IsActive)
            RETURNING id
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var id = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(sql, input, cancellationToken: ct));
        return (await GetAsync(id, ct))!;
    }

    public async Task<ItemCategory?> UpdateAsync(Guid id, int version, ItemCategoryInput input,
        CancellationToken ct = default)
    {
        const string sql = """
            UPDATE item_categories SET code = @Code, name_ar = @NameAr, name_en = @NameEn,
                                       parent_id = @ParentId, is_active = @IsActive
            WHERE id = @id AND version = @version
            RETURNING id
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var updated = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(sql, new
        {
            id, version, input.Code, input.NameAr, input.NameEn, input.ParentId, input.IsActive
        }, cancellationToken: ct));
        return updated is null ? null : await GetAsync(id, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM item_categories WHERE id = @id", new { id }, cancellationToken: ct)) > 0;
    }

    public async Task<ItemCategory?> SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE item_categories SET is_active = @isActive WHERE id = @id", new { id, isActive }, cancellationToken: ct));
        return rows == 0 ? null : await GetAsync(id, ct);
    }

    public async Task<int> CountItemsAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*)::int FROM items WHERE category_id = @id", new { id }, cancellationToken: ct));
    }

    public async Task<int> CountChildrenAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*)::int FROM item_categories WHERE parent_id = @id", new { id }, cancellationToken: ct));
    }
}
