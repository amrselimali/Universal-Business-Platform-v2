using Npgsql;
using Ubp.Core.Results;
using Ubp.Core.Security;

namespace Ubp.Application.Services;

public sealed partial class UserService
{
    public async Task<MutationOutcome> DeleteAsync(ActorContext actor, Guid id,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Users, ActionCodes.Delete, ct: ct);
        var target = await _users.GetDetailAsync(id, ct) ?? throw new NotFoundAppException();
        EnsureTargetVisible(actor, target.User);
        if (id == actor.UserId)
            throw new ValidationAppException("validation.cannot_delete_self");
        if (target.User.IsSuperAdmin && await _users.CountActiveSuperAdminsAsync(ct) <= 1)
            throw new ValidationAppException("validation.last_super_admin");

        var hasLinks = target.BranchIds.Count > 0 || target.WarehouseIds.Count > 0 ||
                       target.RoleIds.Count > 0;
        if (hasLinks || await _users.HasHistoryAsync(id, ct))
        {
            await _users.SetActiveAsync(id, false, ct);
            await _sessions.RevokeAllSessionsAsync(id, ct);
            await _refreshTokens.RevokeAllForUserAsync(id, ct);
            await _audit.WriteAsync(actor, "users", id.ToString(), "UPDATE", target.User, null, ct);
            return new MutationOutcome(false, true, "user.deactivated");
        }

        try
        {
            if (!await _users.DeleteAsync(id, ct)) throw new NotFoundAppException();
            await _audit.WriteAsync(actor, "users", id.ToString(), "DELETE", target.User, null, ct);
            PermissionService.InvalidateAll();
            return new MutationOutcome(true, false, "user.deleted");
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }

    public async Task SetActiveAsync(ActorContext actor, Guid id, bool isActive,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Users, ActionCodes.Edit, ct: ct);
        var target = await _users.GetDetailAsync(id, ct) ?? throw new NotFoundAppException();
        EnsureTargetVisible(actor, target.User);
        if (id == actor.UserId && !isActive)
            throw new ValidationAppException("validation.cannot_deactivate_self");
        if (!isActive && target.User.IsSuperAdmin
            && await _users.CountActiveSuperAdminsAsync(ct) <= 1)
            throw new ValidationAppException("validation.last_super_admin");

        await _users.SetActiveAsync(id, isActive, ct);
        if (!isActive)
        {
            await _sessions.RevokeAllSessionsAsync(id, ct);
            await _refreshTokens.RevokeAllForUserAsync(id, ct);
        }
        PermissionService.InvalidateAll();
        await _audit.WriteAsync(actor, "users", id.ToString(), "UPDATE", target.User,
            new { isActive }, ct);
    }

    public async Task ResetPasswordAsync(ActorContext actor, Guid id, string newPassword,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Users, ActionCodes.ResetPassword, ct: ct);
        var target = await _users.GetDetailAsync(id, ct) ?? throw new NotFoundAppException();
        EnsureTargetVisible(actor, target.User);
        var error = await _settings.ValidatePasswordAsync(newPassword, target.User.Username, ct);
        if (error is not null) throw new ValidationAppException(error);

        await _users.ResetPasswordAsync(id, _hasher.Hash(newPassword), ct);
        await _sessions.RevokeAllSessionsAsync(id, ct);
        await _refreshTokens.RevokeAllForUserAsync(id, ct);
        await _audit.WriteAsync(actor, "users", id.ToString(), "PASSWORD_RESET", null, null, ct);
    }

    public async Task UnlockAsync(ActorContext actor, Guid id, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Users, ActionCodes.Unlock, ct: ct);
        var target = await _users.GetDetailAsync(id, ct) ?? throw new NotFoundAppException();
        EnsureTargetVisible(actor, target.User);
        await _users.UnlockAsync(id, ct);
        await _audit.WriteAsync(actor, "users", id.ToString(), "UPDATE",
            new { lockedUntil = target.User.LockedUntil }, new { lockedUntil = (DateTimeOffset?)null }, ct);
    }
}
