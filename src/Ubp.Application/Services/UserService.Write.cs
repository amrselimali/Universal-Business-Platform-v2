using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;

namespace Ubp.Application.Services;

public sealed partial class UserService
{
    public async Task<UserDetail> CreateAsync(ActorContext actor, UserInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Users, ActionCodes.Add, ct: ct);
        Normalize(input);
        await ValidateCoreAsync(actor, input, null, ct);
        if (string.IsNullOrWhiteSpace(input.Password))
            throw new ValidationAppException("validation.password_required");
        var error = await _settings.ValidatePasswordAsync(input.Password, input.Username, ct);
        if (error is not null) throw new ValidationAppException(error);
        await RequireLinkPermissionsAsync(actor, input, null, ct);
        await ValidateRelationsAsync(actor, input, ct);

        try
        {
            var id = await _users.CreateAsync(input, _hasher.Hash(input.Password), ct);
            PermissionService.InvalidateAll();
            var detail = await _users.GetDetailAsync(id, ct) ?? throw new NotFoundAppException();
            await _audit.WriteAsync(actor, "users", id.ToString(), "INSERT", null, detail.User, ct);
            return detail;
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }

    public async Task<UserDetail> UpdateAsync(ActorContext actor, Guid id, int version, UserInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Users, ActionCodes.Edit, ct: ct);
        var old = await _users.GetDetailAsync(id, ct) ?? throw new NotFoundAppException();
        EnsureTargetVisible(actor, old.User);
        if (id == actor.UserId && (!input.IsActive ||
            input.IsAdmin != old.User.IsAdmin || input.IsSuperAdmin != old.User.IsSuperAdmin))
            throw new ValidationAppException("validation.cannot_change_own_admin");

        Normalize(input);
        await ValidateCoreAsync(actor, input, id, ct);
        await RequireLinkPermissionsAsync(actor, input, old, ct);
        await ValidateRelationsAsync(actor, input, ct);

        if (old.User.IsSuperAdmin && (!input.IsSuperAdmin || !input.IsActive)
            && await _users.CountActiveSuperAdminsAsync(ct) <= 1)
            throw new ValidationAppException("validation.last_super_admin");

        string? hash = null;
        if (!string.IsNullOrWhiteSpace(input.Password))
        {
            var error = await _settings.ValidatePasswordAsync(input.Password, input.Username, ct);
            if (error is not null) throw new ValidationAppException(error);
            hash = _hasher.Hash(input.Password);
        }

        try
        {
            if (!await _users.UpdateAsync(id, version, input, hash, ct))
                throw new ConflictException("error.version_conflict");
            PermissionService.InvalidateAll();
            var updated = await _users.GetDetailAsync(id, ct) ?? throw new NotFoundAppException();
            await _audit.WriteAsync(actor, "users", id.ToString(), "UPDATE", old.User, updated.User, ct);
            if (hash is not null || (old.User.IsActive && !updated.User.IsActive))
            {
                await _sessions.RevokeAllSessionsAsync(id, ct);
                await _refreshTokens.RevokeAllForUserAsync(id, ct);
            }
            return updated;
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }
}
