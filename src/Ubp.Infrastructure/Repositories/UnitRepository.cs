using Dapper;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IUnitRepository
{
    Task<PagedResult<Unit>> ListAsync(PageRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<Unit>> ListOptionsAsync(CancellationToken ct = default);
    Task<Unit?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Unit> CreateAsync(UnitInput input, CancellationToken ct = default);
    Task<Unit?> UpdateAsync(Guid id, int version, UnitInput input, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<Unit?> SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default);
    Task<int> CountUsageAsync(Guid id, CancellationToken ct = default);
}

public sealed class UnitRepository : IUnitRepository
{
    private const string Columns = """
        u.id AS Id, u.code AS Code, u.name_ar AS NameAr, u.name_en AS NameEn,
        u.decimals AS Decimals, u.is_active AS IsActive,
        u.created_at AS CreatedAt, u.updated_at AS UpdatedAt, u.version AS Version
        """;

    private static readonly IReadOnlyDictionary<string, string> SortMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["code"] = "u.code", ["name_ar"] = "u.name_ar", ["decimals"] = "u.decimals"
        };

    private readonly IDbConnectionFactory _factory;

    public UnitRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<PagedResult<Unit>> ListAsync(PageRequest request, CancellationToken ct = default)
    {
        var where = new List<string>();
        if (!request.IncludeInactive) where.Add("u.is_active");
        if (!string.IsNullOrWhiteSpace(request.Search))
            where.Add("u.search_text LIKE '%' || normalize_search(@search) || '%'");

        var whereSql = where.Count == 0 ? "WHERE true" : "WHERE " + string.Join(" AND ", where);
        var order = SqlSort.Build(request.SortBy, SortMap, "u.code", request.Descending);
        var sql = $"""
            SELECT {Columns} FROM units u {whereSql}
            ORDER BY {order} LIMIT @take OFFSET @offset;
            SELECT count(*) FROM units u {whereSql};
            """;

        await using var connection = await _factory.OpenAsync(ct);
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, new
        {
            search = request.Search, take = request.SafePageSize, offset = request.Offset
        }, cancellationToken: ct));
        var items = (await multi.ReadAsync<Unit>()).ToList();
        var total = await multi.ReadFirstAsync<int>();
        return new PagedResult<Unit>
        {
            Items = items, Total = total, Page = request.SafePage, PageSize = request.SafePageSize
        };
    }

    public async Task<IReadOnlyList<Unit>> ListOptionsAsync(CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<Unit>(new CommandDefinition(
            $"SELECT {Columns} FROM units u WHERE u.is_active ORDER BY u.name_ar", cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<Unit?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<Unit>(new CommandDefinition(
            $"SELECT {Columns} FROM units u WHERE u.id = @id", new { id }, cancellationToken: ct));
    }

    public async Task<Unit> CreateAsync(UnitInput input, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO units (code, name_ar, name_en, decimals, is_active)
            VALUES (@Code, @NameAr, @NameEn, @Decimals, @IsActive)
            RETURNING id
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var id = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(sql, input, cancellationToken: ct));
        return (await GetAsync(id, ct))!;
    }

    public async Task<Unit?> UpdateAsync(Guid id, int version, UnitInput input, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE units SET code = @Code, name_ar = @NameAr, name_en = @NameEn,
                             decimals = @Decimals, is_active = @IsActive
            WHERE id = @id AND version = @version
            RETURNING id
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var updated = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(sql, new
        {
            id, version, input.Code, input.NameAr, input.NameEn, input.Decimals, input.IsActive
        }, cancellationToken: ct));
        return updated is null ? null : await GetAsync(id, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM units WHERE id = @id", new { id }, cancellationToken: ct)) > 0;
    }

    public async Task<Unit?> SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE units SET is_active = @isActive WHERE id = @id", new { id, isActive }, cancellationToken: ct));
        return rows == 0 ? null : await GetAsync(id, ct);
    }

    public async Task<int> CountUsageAsync(Guid id, CancellationToken ct = default)
    {
        const string sql = """
            SELECT (SELECT count(*)::int FROM items WHERE base_unit_id = @id)
                 + (SELECT count(*)::int FROM item_units WHERE unit_id = @id)
            """;
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { id }, cancellationToken: ct));
    }
}
