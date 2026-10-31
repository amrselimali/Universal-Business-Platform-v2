using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Results;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface INotificationRepository
{
    Task<Guid> CreateAsync(NotificationInput input, CancellationToken ct = default);
    Task<PagedResult<Notification>> ListAsync(Guid userId, PageRequest request, bool unreadOnly,
        CancellationToken ct = default);
    Task<int> CountUnreadAsync(Guid userId, CancellationToken ct = default);
    Task<bool> MarkReadAsync(Guid id, Guid userId, CancellationToken ct = default);
    Task<int> MarkAllReadAsync(Guid userId, CancellationToken ct = default);
    Task<int> ClearAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> ListRoleUserIdsAsync(Guid roleId, CancellationToken ct = default);
}

/// <summary>
/// إشعارات المستخدم = الموجهة له مباشرة + الموجهة لأي من أدواره.
/// </summary>
public sealed class NotificationRepository : INotificationRepository
{
    private const string Visible = """
        (n.user_id = @userId OR n.role_id IN (SELECT ur.role_id FROM user_roles ur WHERE ur.user_id = @userId))
        """;

    private const string Columns = """
        n.id AS Id, n.user_id AS UserId, n.role_id AS RoleId, n.title_key AS TitleKey,
        n.body AS Body, n.link_url AS LinkUrl, n.severity AS Severity,
        (n.read_at IS NOT NULL) AS IsRead, n.created_at AS CreatedAt
        """;

    private readonly IDbConnectionFactory _factory;

    public NotificationRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<Guid> CreateAsync(NotificationInput input, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO notifications (user_id, role_id, title_key, body, link_url, severity)
            VALUES (@UserId, @RoleId, @TitleKey, @Body, @LinkUrl, @Severity)
            RETURNING id
            """;
        await using var connection = await _factory.OpenAsync(ct);
        try
        {
            return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(sql, new
            {
                input.UserId, input.RoleId, input.TitleKey, input.Body, input.LinkUrl,
                Severity = NotificationSeverities.Normalize(input.Severity)
            }, cancellationToken: ct));
        }
        catch (PostgresException ex) when (ex.SqlState == "23514")
        {
            throw new ValidationAppException("validation.notification_target_invalid");
        }
    }

    public async Task<PagedResult<Notification>> ListAsync(Guid userId, PageRequest request,
        bool unreadOnly, CancellationToken ct = default)
    {
        var filter = unreadOnly ? "AND n.read_at IS NULL" : "";
        var sql = $"""
            SELECT {Columns} FROM notifications n
            WHERE {Visible} {filter}
            ORDER BY n.created_at DESC, n.id DESC
            LIMIT @take OFFSET @offset;
            SELECT count(*)::int FROM notifications n WHERE {Visible} {filter};
            """;
        await using var connection = await _factory.OpenAsync(ct);
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, new
        {
            userId, take = request.SafePageSize, offset = request.Offset
        }, cancellationToken: ct));
        var items = (await multi.ReadAsync<Notification>()).ToList();
        var total = await multi.ReadFirstAsync<int>();
        return new PagedResult<Notification>
        {
            Items = items, Total = total, Page = request.SafePage, PageSize = request.SafePageSize
        };
    }

    public async Task<int> CountUnreadAsync(Guid userId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            $"SELECT count(*)::int FROM notifications n WHERE {Visible} AND n.read_at IS NULL",
            new { userId }, cancellationToken: ct));
    }

    public async Task<bool> MarkReadAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE notifications n SET read_at = now(), read_by = @userId
            WHERE n.id = @id AND n.read_at IS NULL AND {Visible}
            """, new { id, userId }, cancellationToken: ct)) > 0;
    }

    public async Task<int> MarkAllReadAsync(Guid userId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition($"""
            UPDATE notifications n SET read_at = now(), read_by = @userId
            WHERE n.read_at IS NULL AND {Visible}
            """, new { userId }, cancellationToken: ct));
    }

    public async Task<int> ClearAsync(Guid userId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(
            $"DELETE FROM notifications n WHERE {Visible}", new { userId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<Guid>> ListRoleUserIdsAsync(Guid roleId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<Guid>(new CommandDefinition(
            "SELECT user_id FROM user_roles WHERE role_id = @roleId", new { roleId },
            cancellationToken: ct));
        return rows.ToList();
    }
}
