using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Ubp.Application.Services;

public sealed partial class QaService
{
    private async Task<(bool, string)> DbTestAsync(
        Func<NpgsqlConnection, NpgsqlTransaction, Task<bool>> body, CancellationToken ct)
    {
        NpgsqlConnection? connection = null;
        NpgsqlTransaction? tx = null;
        try
        {
            connection = await _qa.OpenAsync(ct);
            tx = await connection.BeginTransactionAsync(ct);
            var passed = await body(connection, tx);
            await tx.RollbackAsync(ct);
            return (passed, passed ? "OK" : "Assertion failed");
        }
        catch (Exception ex)
        {
            if (tx is not null) await tx.RollbackAsync(ct);
            _logger.LogWarning(ex, "QA database check failed");
            return (false, ex.GetBaseException().Message.Length > 300
                ? ex.GetBaseException().Message[..300]
                : ex.GetBaseException().Message);
        }
        finally
        {
            if (tx is not null) await tx.DisposeAsync();
            if (connection is not null) await connection.DisposeAsync();
        }
    }

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, NpgsqlTransaction tx,
        string sql) =>
        (await connection.ExecuteScalarAsync<T>(new CommandDefinition(sql, transaction: tx)))!;

    private async Task<(bool, string)> AuditImmutabilityAsync(CancellationToken ct)
    {
        return await DbTestAsync(async (connection, tx) =>
        {
            var id = await connection.ExecuteScalarAsync<long>(new CommandDefinition("""
                INSERT INTO audit_log (table_name, record_id, action)
                VALUES ('qa', 'qa', 'INSERT') RETURNING id
                """, transaction: tx, cancellationToken: ct));
            try
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE audit_log SET action = 'DELETE' WHERE id = @id",
                    new { id }, tx, cancellationToken: ct));
                return false;
            }
            catch (PostgresException)
            {
                return true;
            }
        }, ct);
    }

    private async Task<(bool, string)> VersionConcurrencyAsync(CancellationToken ct)
    {
        return await DbTestAsync(async (connection, tx) =>
        {
            var code = "QA-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            var id = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition("""
                INSERT INTO branches (code, name_ar) VALUES (@code, 'QA Branch') RETURNING id
                """, new { code }, tx, cancellationToken: ct));
            var version = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
                UPDATE branches SET name_ar = 'QA Branch 2' WHERE id = @id RETURNING version
                """, new { id }, tx, cancellationToken: ct));
            return version == 2;
        }, ct);
    }
}
