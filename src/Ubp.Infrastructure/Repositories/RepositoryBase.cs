using Dapper;
using Ubp.Core.Paging;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

/// <summary>
/// اختصارات Dapper المشتركة بين مستودعات البيانات الأساسية (المرحلة 4A-1)
/// لتقليل التكرار مع الحفاظ على استعلامات صريحة ومُعاملة بالباراميترات.
/// </summary>
public abstract class RepositoryBase
{
    protected readonly IDbConnectionFactory Factory;
    protected RepositoryBase(IDbConnectionFactory factory) => Factory = factory;

    protected async Task<PagedResult<T>> PageAsync<T>(string selectSql, string countSql, object param,
        PageRequest request, CancellationToken ct)
    {
        await using var connection = await Factory.OpenAsync(ct);
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(
            selectSql + ";" + countSql, param, cancellationToken: ct));
        var items = (await multi.ReadAsync<T>()).ToList();
        var total = await multi.ReadFirstAsync<int>();
        return new PagedResult<T>
        {
            Items = items, Total = total, Page = request.SafePage, PageSize = request.SafePageSize
        };
    }

    protected async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? param, CancellationToken ct)
    {
        await using var connection = await Factory.OpenAsync(ct);
        return (await connection.QueryAsync<T>(new CommandDefinition(sql, param, cancellationToken: ct)))
            .ToList();
    }

    protected async Task<T?> SingleAsync<T>(string sql, object param, CancellationToken ct)
    {
        await using var connection = await Factory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<T>(
            new CommandDefinition(sql, param, cancellationToken: ct));
    }

    protected async Task<Guid> InsertRowAsync(string sql, object param, CancellationToken ct)
    {
        await using var connection = await Factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(sql, param, cancellationToken: ct));
    }

    protected async Task<Guid?> UpdateRowAsync(string sql, object param, CancellationToken ct)
    {
        await using var connection = await Factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(sql, param, cancellationToken: ct));
    }

    protected async Task<int> ExecuteRowAsync(string sql, object param, CancellationToken ct)
    {
        await using var connection = await Factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(sql, param, cancellationToken: ct));
    }
}
