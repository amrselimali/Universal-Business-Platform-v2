using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IPaymentTermRepository
{
    Task<PagedResult<PaymentTerm>> ListAsync(PageRequest request, bool includeInactive, CancellationToken ct = default);
    Task<IReadOnlyList<PaymentTerm>> ListOptionsAsync(CancellationToken ct = default);
    Task<PaymentTerm?> GetAsync(Guid id, CancellationToken ct = default);
    Task<PaymentTerm?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<Guid> CreateAsync(Guid id, PaymentTermInput input, Guid userId, CancellationToken ct = default);
    Task<PaymentTerm?> UpdateAsync(Guid id, int version, PaymentTermInput input, Guid userId, CancellationToken ct = default);
    Task<PaymentTerm?> SetActiveAsync(Guid id, bool isActive, Guid userId, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed class PaymentTermRepository : RepositoryBase, IPaymentTermRepository
{
    private const string Columns = """
        p.id AS Id, p.code AS Code, p.name_ar AS NameAr, p.name_en AS NameEn,
        p.net_days AS NetDays, p.discount_percent AS DiscountPercent,
        p.discount_days AS DiscountDays, p.notes AS Notes, p.is_active AS IsActive,
        p.version AS Version
        FROM payment_terms p
        """;

    public PaymentTermRepository(IDbConnectionFactory factory) : base(factory) { }

    public async Task<PagedResult<PaymentTerm>> ListAsync(PageRequest request, bool includeInactive,
        CancellationToken ct = default)
    {
        var where = includeInactive ? "WHERE true" : "WHERE p.is_active";
        if (!string.IsNullOrWhiteSpace(request.Search))
            where += " AND (p.code ILIKE '%' || @search || '%' OR normalize_search(p.name_ar) " +
                     "LIKE '%' || normalize_search(@search) || '%')";
        return await PageAsync<PaymentTerm>(
            $"SELECT {Columns} {where} ORDER BY p.code LIMIT @take OFFSET @offset",
            $"SELECT count(*)::int FROM payment_terms p {where}",
            new { search = request.Search, take = request.SafePageSize, offset = request.Offset },
            request, ct);
    }

    public Task<IReadOnlyList<PaymentTerm>> ListOptionsAsync(CancellationToken ct = default) =>
        QueryAsync<PaymentTerm>($"SELECT {Columns} WHERE p.is_active ORDER BY p.net_days, p.code", null, ct);

    public Task<PaymentTerm?> GetAsync(Guid id, CancellationToken ct = default) =>
        SingleAsync<PaymentTerm>($"SELECT {Columns} WHERE p.id = @id", new { id }, ct);

    public Task<PaymentTerm?> GetByCodeAsync(string code, CancellationToken ct = default) =>
        SingleAsync<PaymentTerm>($"SELECT {Columns} WHERE p.code = @code", new { code }, ct);

    public async Task<Guid> CreateAsync(Guid id, PaymentTermInput i, Guid userId,
        CancellationToken ct = default) => await InsertRowAsync("""
            INSERT INTO payment_terms (id, code, name_ar, name_en, net_days, discount_percent,
                discount_days, notes, is_active, created_by)
            VALUES (@id, @Code, @NameAr, @NameEn, @NetDays, @DiscountPercent, @DiscountDays,
                    @Notes, @IsActive, @userId)
            RETURNING id
            """, new { id, i.Code, i.NameAr, i.NameEn, i.NetDays, i.DiscountPercent,
                i.DiscountDays, i.Notes, i.IsActive, userId }, ct);

    public async Task<PaymentTerm?> UpdateAsync(Guid id, int version, PaymentTermInput i, Guid userId,
        CancellationToken ct = default)
    {
        var updated = await UpdateRowAsync("""
            UPDATE payment_terms SET code = @Code, name_ar = @NameAr, name_en = @NameEn,
                net_days = @NetDays, discount_percent = @DiscountPercent,
                discount_days = @DiscountDays, notes = @Notes, is_active = @IsActive,
                updated_by = @userId
            WHERE id = @id AND version = @version RETURNING id
            """, new { id, version, i.Code, i.NameAr, i.NameEn, i.NetDays, i.DiscountPercent,
                i.DiscountDays, i.Notes, i.IsActive, userId }, ct);
        return updated is null ? null : await GetAsync(id, ct);
    }

    public async Task<PaymentTerm?> SetActiveAsync(Guid id, bool isActive, Guid userId,
        CancellationToken ct = default)
    {
        var rows = await ExecuteRowAsync(
            "UPDATE payment_terms SET is_active = @isActive, updated_by = @userId WHERE id = @id",
            new { id, isActive, userId }, ct);
        return rows == 0 ? null : await GetAsync(id, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default) =>
        await ExecuteRowAsync("DELETE FROM payment_terms WHERE id = @id", new { id }, ct) > 0;
}
