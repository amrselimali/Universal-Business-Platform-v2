using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Results;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface ITaxRepository
{
    Task<PagedResult<Tax>> ListAsync(PageRequest request, bool includeInactive, CancellationToken ct = default);
    Task<IReadOnlyList<Tax>> ListOptionsAsync(CancellationToken ct = default);
    Task<Tax?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Tax?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<Guid> CreateAsync(Guid id, TaxInput input, Guid userId, CancellationToken ct = default);
    Task<Tax?> UpdateAsync(Guid id, int version, TaxInput input, Guid userId, CancellationToken ct = default);
    Task<Tax?> SetActiveAsync(Guid id, bool isActive, Guid userId, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed class TaxRepository : RepositoryBase, ITaxRepository
{
    private const string Columns = """
        t.id AS Id, t.code AS Code, t.name_ar AS NameAr, t.name_en AS NameEn, t.rate AS Rate,
        t.is_inclusive_allowed AS IsInclusiveAllowed, t.output_account_id AS OutputAccountId,
        t.input_account_id AS InputAccountId, t.notes AS Notes, t.is_active AS IsActive,
        t.version AS Version, (SELECT count(*)::int FROM items i WHERE i.tax_id = t.id) AS ItemsCount
        FROM taxes t
        """;

    private const string From = "FROM taxes t";

    public TaxRepository(IDbConnectionFactory factory) : base(factory) { }

    public async Task<PagedResult<Tax>> ListAsync(PageRequest request, bool includeInactive,
        CancellationToken ct = default)
    {
        var where = includeInactive ? "WHERE true" : "WHERE t.is_active";
        if (!string.IsNullOrWhiteSpace(request.Search))
            where += " AND (t.code ILIKE '%' || @search || '%' OR normalize_search(t.name_ar) " +
                     "LIKE '%' || normalize_search(@search) || '%')";
        var select = $"SELECT {Columns} {where} ORDER BY t.code LIMIT @take OFFSET @offset";
        var count = $"SELECT count(*)::int {From} {where}";
        return await PageAsync<Tax>(select, count, new
        { search = request.Search, take = request.SafePageSize, offset = request.Offset }, request, ct);
    }

    public Task<IReadOnlyList<Tax>> ListOptionsAsync(CancellationToken ct = default) =>
        QueryAsync<Tax>($"SELECT {Columns} WHERE t.is_active ORDER BY t.rate, t.code", null, ct);

    public Task<Tax?> GetAsync(Guid id, CancellationToken ct = default) =>
        SingleAsync<Tax>($"SELECT {Columns} WHERE t.id = @id", new { id }, ct);

    public Task<Tax?> GetByCodeAsync(string code, CancellationToken ct = default) =>
        SingleAsync<Tax>($"SELECT {Columns} WHERE t.code = @code", new { code }, ct);

    public async Task<Guid> CreateAsync(Guid id, TaxInput i, Guid userId, CancellationToken ct = default) =>
        await InsertRowAsync("""
            INSERT INTO taxes (id, code, name_ar, name_en, rate, is_inclusive_allowed,
                output_account_id, input_account_id, notes, is_active, created_by)
            VALUES (@id, @Code, @NameAr, @NameEn, @Rate, @IsInclusiveAllowed,
                    @OutputAccountId, @InputAccountId, @Notes, @IsActive, @userId)
            RETURNING id
            """, new { id, i.Code, i.NameAr, i.NameEn, i.Rate, i.IsInclusiveAllowed,
                i.OutputAccountId, i.InputAccountId, i.Notes, i.IsActive, userId }, ct);

    public async Task<Tax?> UpdateAsync(Guid id, int version, TaxInput i, Guid userId,
        CancellationToken ct = default)
    {
        var updated = await UpdateRowAsync("""
            UPDATE taxes SET code = @Code, name_ar = @NameAr, name_en = @NameEn, rate = @Rate,
                is_inclusive_allowed = @IsInclusiveAllowed, output_account_id = @OutputAccountId,
                input_account_id = @InputAccountId, notes = @Notes, is_active = @IsActive,
                updated_by = @userId
            WHERE id = @id AND version = @version RETURNING id
            """, new { id, version, i.Code, i.NameAr, i.NameEn, i.Rate, i.IsInclusiveAllowed,
                i.OutputAccountId, i.InputAccountId, i.Notes, i.IsActive, userId }, ct);
        return updated is null ? null : await GetAsync(id, ct);
    }

    public async Task<Tax?> SetActiveAsync(Guid id, bool isActive, Guid userId,
        CancellationToken ct = default)
    {
        var rows = await ExecuteRowAsync(
            "UPDATE taxes SET is_active = @isActive, updated_by = @userId WHERE id = @id",
            new { id, isActive, userId }, ct);
        return rows == 0 ? null : await GetAsync(id, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default) =>
        await ExecuteRowAsync("DELETE FROM taxes WHERE id = @id", new { id }, ct) > 0;
}
