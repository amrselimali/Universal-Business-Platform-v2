using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class AccountRepository
{
    public async Task<Account?> MoveAsync(Guid id, Guid? newParentId, Guid userId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var node = await connection.QuerySingleOrDefaultAsync<MoveRow>(new CommandDefinition(
            "SELECT id AS Id, code AS Code, path AS Path, level AS Level, parent_id AS ParentId " +
            "FROM accounts WHERE id = @id FOR UPDATE", new { id }, tx, cancellationToken: ct));
        if (node is null)
        {
            await tx.RollbackAsync(ct);
            return null;
        }

        var parentPrefix = "/";
        short parentLevel = 0;
        if (newParentId.HasValue)
        {
            var parent = await connection.QuerySingleOrDefaultAsync<MoveRow>(new CommandDefinition(
                "SELECT id AS Id, code AS Code, path AS Path, level AS Level, parent_id AS ParentId " +
                "FROM accounts WHERE id = @pid", new { pid = newParentId.Value }, tx, cancellationToken: ct));
            if (parent is null || parent.Id == id || parent.Path.StartsWith(node.Path, StringComparison.Ordinal))
            {
                await tx.RollbackAsync(ct);
                return null;
            }
            parentPrefix = parent.Path;
            parentLevel = parent.Level;
        }

        var newPath = parentPrefix + node.Code + "/";
        var newLevel = (short)(parentLevel + 1);
        var delta = (short)(newLevel - node.Level);

        await MoveSubtreeAsync(connection, tx, id, node.Path, newPath, delta, ct);

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE accounts SET parent_id = @newParentId, path = @newPath, level = @newLevel,
                                updated_by = @userId
            WHERE id = @id
            """, new { id, newParentId, newPath, newLevel, userId }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>تحديث مسار الشجرة الفرعية ومستوياتها في عبارة واحدة داخل نفس المعاملة.</summary>
    private static async Task MoveSubtreeAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        Guid id, string oldPath, string newPath, short levelDelta, CancellationToken ct)
    {
        if (string.Equals(oldPath, newPath, StringComparison.Ordinal)) return;
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE accounts
               SET path = @newPath || substring(path from char_length(@oldPath) + 1),
                   level = greatest(1, level + @levelDelta)::smallint
             WHERE path LIKE @oldPath || '%' AND id <> @id
            """, new { newPath, oldPath, levelDelta, id }, tx, cancellationToken: ct));
    }

    private sealed class MoveRow
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = "";
        public string Path { get; set; } = "/";
        public short Level { get; set; }
        public Guid? ParentId { get; set; }
    }
}
