using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Infrastructure.Audit;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IAuditRepository
{
    Task WriteAsync(AuditWrite entry, CancellationToken ct = default);
    Task<KeysetPage<AuditEntry>> ListAsync(AuditFilter filter, short scope, Guid actorId,
        IReadOnlyCollection<Guid> branchIds, bool isAdmin, int limit = 25, CancellationToken ct = default);
}

public sealed class AuditRepository : IAuditRepository
{
    private readonly IDbConnectionFactory _factory;

    public AuditRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task WriteAsync(AuditWrite entry, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO audit_log
                (user_id, branch_id, table_name, record_id, action, old_data, new_data, ip_address, trace_id)
            VALUES
                (@UserId, @BranchId, @TableName, @RecordId, @Action,
                 CAST(@OldData AS jsonb), CAST(@NewData AS jsonb),
                 CAST(@IpAddress AS inet), @TraceId)
            """;

        await using var connection = await _factory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            entry.UserId,
            entry.BranchId,
            entry.TableName,
            entry.RecordId,
            entry.Action,
            OldData = AuditSanitizer.ToJson(entry.OldData),
            NewData = AuditSanitizer.ToJson(entry.NewData),
            IpAddress = string.IsNullOrWhiteSpace(entry.IpAddress) ? null : entry.IpAddress,
            entry.TraceId
        }, cancellationToken: ct));
    }

    public async Task<KeysetPage<AuditEntry>> ListAsync(AuditFilter filter, short scope, Guid actorId,
        IReadOnlyCollection<Guid> branchIds, bool isAdmin, int limit = 25, CancellationToken ct = default)
    {
        limit = limit is < 1 or > 25 ? 25 : limit;
        var where = new List<string> { "(@beforeId IS NULL OR a.id < @beforeId)" };
        if (filter.From.HasValue) where.Add("a.created_at >= @from");
        if (filter.To.HasValue) where.Add("a.created_at <= @to");
        if (filter.UserId.HasValue) where.Add("a.user_id = @filterUser");
        if (filter.BranchId.HasValue) where.Add("a.branch_id = @filterBranch");
        if (!string.IsNullOrWhiteSpace(filter.TableName)) where.Add("a.table_name = @tableName");
        if (!string.IsNullOrWhiteSpace(filter.Action)) where.Add("a.action = @action");

        if (!isAdmin)
        {
            if (scope <= 0) where.Add("a.user_id = @actorId");
            else if (scope == 1) where.Add("(a.user_id = @actorId OR a.branch_id = ANY(@branchIds))");
        }

        var sql = $"""
            SELECT a.id, a.user_id AS UserId, u.username AS Username,
                   a.branch_id AS BranchId, b.name_ar AS BranchName,
                   a.table_name AS TableName, a.record_id AS RecordId, a.action AS Action,
                   a.old_data::text AS OldData, a.new_data::text AS NewData,
                   host(a.ip_address) AS IpAddress, a.trace_id AS TraceId, a.created_at AS CreatedAt
            FROM audit_log a
            LEFT JOIN users u ON u.id = a.user_id
            LEFT JOIN branches b ON b.id = a.branch_id
            WHERE {string.Join(" AND ", where)}
            ORDER BY a.id DESC
            LIMIT @take
            """;

        await using var connection = await _factory.OpenAsync(ct);
        var rows = (await connection.QueryAsync<AuditEntry>(new CommandDefinition(sql, new
        {
            beforeId = filter.BeforeId,
            from = filter.From,
            to = filter.To,
            filterUser = filter.UserId,
            filterBranch = filter.BranchId,
            tableName = filter.TableName,
            action = filter.Action,
            actorId,
            branchIds = branchIds.ToArray(),
            take = limit + 1
        }, cancellationToken: ct))).ToList();

        var hasMore = rows.Count > limit;
        if (hasMore) rows.RemoveAt(rows.Count - 1);
        return new KeysetPage<AuditEntry>
        {
            Items = rows,
            HasMore = hasMore,
            NextBeforeId = hasMore ? rows[^1].Id : null,
            PageSize = limit
        };
    }
}
