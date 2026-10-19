using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

public sealed partial class DatabaseAdminService
{
    public async Task UpdateRowAsync(ActorContext actor, string table, string id,
        Dictionary<string, object?> values, string confirmation, CancellationToken ct = default)
    {
        RequireSuperAdmin(actor);
        await _permissions.RequireAsync(actor, ScreenCodes.DatabaseAdmin, ActionCodes.EditRows, ct: ct);
        if (!string.Equals(confirmation, $"UPDATE {table}", StringComparison.Ordinal))
            throw new ValidationAppException("validation.confirmation_mismatch");

        var old = await _schema.GetRowAsync(table, id, ct) ?? throw new NotFoundAppException();
        try
        {
            if (!await _schema.UpdateRowAsync(table, id, values, ct))
                throw new ConflictException("error.version_conflict");
        }
        catch (InvalidOperationException ex)
        {
            throw new ValidationAppException(ex.Message == "TABLE_READ_ONLY"
                ? "validation.table_read_only" : "validation.table_not_allowed");
        }
        catch (Npgsql.PostgresException ex) { throw DbErrorMapper.Map(ex); }

        var updatedRow = await _schema.GetRowAsync(table, id, ct);
        await _audit.WriteAsync(actor, table, id, "UPDATE", old, updatedRow, ct);
        InvalidateCaches(table);
    }

    public async Task DeleteRowAsync(ActorContext actor, string table, string id, string confirmation,
        CancellationToken ct = default)
    {
        RequireSuperAdmin(actor);
        await _permissions.RequireAsync(actor, ScreenCodes.DatabaseAdmin, ActionCodes.EditRows, ct: ct);
        if (!string.Equals(confirmation, $"DELETE {table}", StringComparison.Ordinal))
            throw new ValidationAppException("validation.confirmation_mismatch");

        var old = await _schema.GetRowAsync(table, id, ct) ?? throw new NotFoundAppException();
        try
        {
            if (!await _schema.DeleteRowAsync(table, id, ct)) throw new NotFoundAppException();
        }
        catch (InvalidOperationException ex)
        {
            throw new ValidationAppException(ex.Message == "TABLE_READ_ONLY"
                ? "validation.table_read_only" : "validation.table_not_allowed");
        }
        catch (Npgsql.PostgresException ex) { throw DbErrorMapper.Map(ex); }

        await _audit.WriteAsync(actor, table, id, "DELETE", old, null, ct);
        InvalidateCaches(table);
    }

    private void InvalidateCaches(string table)
    {
        if (table == "app_settings") _settings.Invalidate();
        if (table is "users" or "user_permissions" or "role_permissions" or "user_roles" or "roles")
            PermissionService.InvalidateAll();
    }
}
