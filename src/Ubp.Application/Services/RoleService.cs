using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

public sealed partial class RoleService
{
    private readonly IRoleRepository _repository;
    private readonly PermissionService _permissions;
    private readonly AuditService _audit;

    public RoleService(IRoleRepository repository, PermissionService permissions, AuditService audit)
    {
        _repository = repository; _permissions = permissions; _audit = audit;
    }

    public async Task<PagedResult<Role>> ListAsync(ActorContext actor, PageRequest request,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Roles, ActionCodes.View, ct: ct);
        return await _repository.ListAsync(request, ct);
    }

    public async Task<IReadOnlyList<Role>> OptionsAsync(ActorContext actor, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Users, ActionCodes.View, ct: ct);
        return await _repository.ListOptionsAsync(ct);
    }

    public async Task<IReadOnlyList<PermissionEntry>> GetPermissionsAsync(ActorContext actor,
        Guid roleId, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Roles, ActionCodes.View, ct: ct);
        return await _repository.GetPermissionsAsync(roleId, ct);
    }

    public async Task<Role> CreateAsync(ActorContext actor, RoleInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Roles, ActionCodes.Add, ct: ct);
        Normalize(input);
        Validate(input);
        try
        {
            var id = await _repository.CreateAsync(input, ct);
            PermissionService.InvalidateAll();
            var created = await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();
            await _audit.WriteAsync(actor, "roles", id.ToString(), "INSERT", null, created, ct);
            if (input.Permissions.Count > 0)
                await _audit.WriteAsync(actor, "role_permissions", id.ToString(), "PERMISSION_CHANGE", null,
                    input.Permissions, ct);
            return created;
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }

    public async Task<Role> UpdateAsync(ActorContext actor, Guid id, int version, RoleInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Roles, ActionCodes.Edit, ct: ct);
        Normalize(input);
        Validate(input);
        var old = await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();
        var oldPermissions = await _repository.GetPermissionsAsync(id, ct);
        try
        {
            if (!await _repository.UpdateAsync(id, version, input, ct))
                throw new ConflictException("error.version_conflict");
            PermissionService.InvalidateAll();
            var updated = await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();
            await _audit.WriteAsync(actor, "roles", id.ToString(), "UPDATE", old, updated, ct);
            if (!oldPermissions.SequenceEqual(input.Permissions))
                await _audit.WriteAsync(actor, "role_permissions", id.ToString(), "PERMISSION_CHANGE",
                    oldPermissions, input.Permissions, ct);
            return updated;
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }
}
