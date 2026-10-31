using Dapper;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class TreasuryRepository
{
    /// <summary>يجعل الخزينة رئيسية ويلغي رئيسية غيرها في نفس المعاملة (فهرس فريد يضمن واحدة فقط).</summary>
    public async Task<bool> SetMainAsync(Guid treasuryId, Guid userId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE treasuries SET is_main = false WHERE is_main AND id <> @treasuryId",
            new { treasuryId }, tx, cancellationToken: ct));

        var updated = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
            UPDATE treasuries SET is_main = true, updated_by = @userId, updated_at = now()
            WHERE id = @treasuryId AND is_active
            RETURNING id
            """, new { treasuryId, userId }, tx, cancellationToken: ct));

        if (updated is null)
        {
            await tx.RollbackAsync(ct);
            return false;
        }

        await tx.CommitAsync(ct);
        return true;
    }

    /// <summary>الخزينة الرئيسية الحالية (أو null).</summary>
    public async Task<Guid?> GetMainAsync(CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT id FROM treasuries WHERE is_main LIMIT 1", cancellationToken: ct));
    }
}
