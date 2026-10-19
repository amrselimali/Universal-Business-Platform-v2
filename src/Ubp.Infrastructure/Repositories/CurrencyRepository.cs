using Dapper;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface ICurrencyRepository
{
    Task<PagedResult<Currency>> ListAsync(PageRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<Currency>> ListAllAsync(CancellationToken ct = default);
    Task<Currency?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Currency> CreateAsync(CurrencyInput input, CancellationToken ct = default);
    Task<Currency?> UpdateAsync(Guid id, int version, CurrencyInput input, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed class CurrencyRepository : ICurrencyRepository
{
    private readonly IDbConnectionFactory _factory;

    private static readonly IReadOnlyDictionary<string, string> SortMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["code"] = "currency_code", ["name_ar"] = "name_ar", ["name_en"] = "name_en",
            ["decimals"] = "decimals", ["is_base"] = "is_base"
        };

    private const string Columns = """
        id AS Id, currency_code AS CurrencyCode, name_ar AS NameAr, name_en AS NameEn,
        symbol AS Symbol, decimals AS Decimals, is_base AS IsBase,
        created_at AS CreatedAt, updated_at AS UpdatedAt, version AS Version
        """;

    public CurrencyRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<PagedResult<Currency>> ListAsync(PageRequest request, CancellationToken ct = default)
    {
        var where = "WHERE (@search IS NULL OR normalize_search(currency_code || ' ' || name_ar || ' ' || coalesce(name_en,'')) LIKE '%' || normalize_search(@search) || '%')";
        var order = SqlSort.Build(request.SortBy, SortMap, "is_base", true);
        var sql = $"""
            SELECT {Columns} FROM currencies {where}
            ORDER BY {order} LIMIT @take OFFSET @offset;
            SELECT count(*) FROM currencies {where};
            """;
        await using var connection = await _factory.OpenAsync(ct);
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, new
        {
            search = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search,
            take = request.SafePageSize,
            offset = request.Offset
        }, cancellationToken: ct));
        var items = (await multi.ReadAsync<Currency>()).ToList();
        var total = await multi.ReadFirstAsync<int>();
        return new PagedResult<Currency> { Items = items, Total = total, Page = request.SafePage, PageSize = request.SafePageSize };
    }

    public async Task<IReadOnlyList<Currency>> ListAllAsync(CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var items = await connection.QueryAsync<Currency>(
            new CommandDefinition($"SELECT {Columns} FROM currencies ORDER BY is_base DESC, currency_code", cancellationToken: ct));
        return items.ToList();
    }

    public async Task<Currency?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<Currency>(
            new CommandDefinition($"SELECT {Columns} FROM currencies WHERE id = @id", new { id }, cancellationToken: ct));
    }

    public async Task<Currency> CreateAsync(CurrencyInput input, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO currencies (currency_code, name_ar, name_en, symbol, decimals, is_base)
            VALUES (@CurrencyCode, @NameAr, @NameEn, @Symbol, @Decimals, @IsBase)
            RETURNING id
            """;
        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        if (input.IsBase)
            await connection.ExecuteAsync("UPDATE currencies SET is_base = false WHERE is_base", transaction: tx);
        var id = await connection.ExecuteScalarAsync<Guid>(sql, input, tx);
        await tx.CommitAsync(ct);
        return (await GetAsync(id, ct))!;
    }

    public async Task<Currency?> UpdateAsync(Guid id, int version, CurrencyInput input, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE currencies SET
                currency_code = @CurrencyCode, name_ar = @NameAr, name_en = @NameEn,
                symbol = @Symbol, decimals = @Decimals, is_base = @IsBase
            WHERE id = @id AND version = @version
            RETURNING id
            """;
        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        if (input.IsBase)
            await connection.ExecuteAsync(
                "UPDATE currencies SET is_base = false WHERE is_base AND id <> @id", new { id }, tx);
        var updated = await connection.ExecuteScalarAsync<Guid?>(sql, new
        {
            id, version, input.CurrencyCode, input.NameAr, input.NameEn, input.Symbol,
            input.Decimals, input.IsBase
        }, tx);
        if (updated is null)
        {
            await tx.RollbackAsync(ct);
            return null;
        }
        await tx.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.ExecuteAsync(
            new CommandDefinition("DELETE FROM currencies WHERE id = @id AND NOT is_base",
                new { id }, cancellationToken: ct));
        return rows > 0;
    }
}
