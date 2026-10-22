using Dapper;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IJournalPostingRepository
{
    Task<Guid> PostAsync(Guid entryId, Guid userId, CancellationToken ct = default);
    Task<Guid> ReverseAsync(Guid entryId, Guid userId, DateTime reversalDate,
        CancellationToken ct = default);
    Task<decimal> GetOpeningBalanceAsync(Guid accountId, Guid? branchId, DateTime before,
        bool includeChildren, CancellationToken ct = default);
    Task<DateTime?> GetStartDateAsync(CancellationToken ct = default);
}

/// <summary>
/// كل المنطق الحسّاس داخل دوال PostgreSQL: الترحيل والعكس يحدثان في معاملة واحدة
/// مع الأرصدة الشهرية وأرصدة الأطراف، فلا يمكن أن يوجد قيد مرحّل بلا رصيد.
/// </summary>
public sealed class JournalPostingRepository : IJournalPostingRepository
{
    private readonly IDbConnectionFactory _factory;

    public JournalPostingRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<Guid> PostAsync(Guid entryId, Guid userId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT fn_gl_post_entry(@entryId, @userId)", new { entryId, userId }, tx,
            cancellationToken: ct));
        await tx.CommitAsync(ct);
        return entryId;
    }

    public async Task<Guid> ReverseAsync(Guid entryId, Guid userId, DateTime reversalDate,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        var newId = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            "SELECT fn_gl_reverse_entry(@entryId, @userId, @reversalDate)",
            new { entryId, userId, reversalDate }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return newId;
    }

    public async Task<decimal> GetOpeningBalanceAsync(Guid accountId, Guid? branchId, DateTime before,
        bool includeChildren, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<decimal>(new CommandDefinition(
            "SELECT fn_gl_opening_balance(@accountId, @branchId, @before, @includeChildren)",
            new { accountId, branchId, before, includeChildren }, cancellationToken: ct));
    }

    public async Task<DateTime?> GetStartDateAsync(CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<DateTime?>(new CommandDefinition(
            "SELECT accounting_start_date FROM app_settings LIMIT 1", cancellationToken: ct));
    }
}
