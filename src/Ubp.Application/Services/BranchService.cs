using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

public sealed partial class BranchService
{
    private readonly IBranchRepository _repository;
    private readonly PermissionService _permissions;
    private readonly CustomFieldService _customFields;
    private readonly AuditService _audit;

    public BranchService(IBranchRepository repository, PermissionService permissions,
        CustomFieldService customFields, AuditService audit)
    {
        _repository = repository; _permissions = permissions;
        _customFields = customFields; _audit = audit;
    }

    public async Task<PagedResult<Branch>> ListAsync(ActorContext actor, PageRequest request,
        CancellationToken ct = default)
    {
        var scope = await _permissions.GetScopeAsync(actor, ScreenCodes.Branches, ActionCodes.View, ct);
        if (scope < 0) throw new ForbiddenException();
        return await _repository.ListAsync(request, scope, actor.UserId,
            actor.AllowedBranchIds, actor.IsAdmin, ct);
    }

    public async Task<IReadOnlyList<Branch>> OptionsAsync(ActorContext actor,
        CancellationToken ct = default) =>
        await _repository.ListOptionsAsync(actor.AllowedBranchIds, actor.IsAdmin, ct);

    public async Task<Branch?> GetAsync(ActorContext actor, Guid id, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Branches, ActionCodes.View, ct: ct);
        if (!actor.CanAccessBranch(id)) throw new ForbiddenException();
        return await _repository.GetAsync(id, ct);
    }

    public async Task<Branch> CreateAsync(ActorContext actor, BranchInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Branches, ActionCodes.Add, ct: ct);
        Normalize(input);
        Validate(input);
        input.CustomFields = await _customFields.ValidateValuesAsync("branches", input.CustomFields, ct);
        try
        {
            var created = await _repository.CreateAsync(input, ct);
            await _audit.WriteAsync(actor, "branches", created.Id.ToString(), "INSERT", null, created, ct);
            return created;
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }

    public async Task<Branch> UpdateAsync(ActorContext actor, Guid id, int version,
        BranchInput input, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Branches, ActionCodes.Edit, ct: ct);
        if (!actor.CanAccessBranch(id)) throw new ForbiddenException();
        Normalize(input);
        Validate(input);
        input.CustomFields = await _customFields.ValidateValuesAsync("branches", input.CustomFields, ct);
        var old = await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();
        try
        {
            var updated = await _repository.UpdateAsync(id, version, input, ct)
                ?? throw new ConflictException("error.version_conflict");
            await _audit.WriteAsync(actor, "branches", id.ToString(), "UPDATE", old, updated, ct);
            return updated;
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }
}
