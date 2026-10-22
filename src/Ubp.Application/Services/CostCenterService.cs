using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

public sealed partial class CostCenterService
{
    private readonly ICostCenterRepository _repository;
    private readonly PermissionService _permissions;
    private readonly AuditService _audit;

    public CostCenterService(ICostCenterRepository repository, PermissionService permissions,
        AuditService audit)
    {
        _repository = repository; _permissions = permissions; _audit = audit;
    }

    public async Task<PagedResult<CostCenter>> ListAsync(ActorContext actor, CostCenterFilter filter,
        PageRequest request, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.CostCenters, ActionCodes.View, ct: ct);
        return await _repository.ListAsync(filter, request, ct);
    }

    public async Task<IReadOnlyList<CostCenter>> OptionsAsync(ActorContext actor, Guid? branchId,
        CancellationToken ct = default)
    {
        var allowed = await _permissions.HasAsync(actor, ScreenCodes.CostCenters, ActionCodes.View, ct)
                   || await _permissions.HasAsync(actor, ScreenCodes.JournalEntry, ActionCodes.View, ct);
        if (!allowed) throw new ForbiddenException();
        return await _repository.ListOptionsAsync(branchId, ct);
    }

    public async Task<CostCenter> GetAsync(ActorContext actor, Guid id, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.CostCenters, ActionCodes.View, ct: ct);
        return await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();
    }

    public async Task<CostCenter> CreateRootAsync(ActorContext actor, CostCenterInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.CostCenters, ActionCodes.CreateRoot, ct: ct);
        Normalize(input);
        Validate(input);
        return await CreateCoreAsync(actor, input, $"/{input.Code}/", 1, ct);
    }

    public async Task<CostCenter> CreateChildAsync(ActorContext actor, Guid parentId,
        CostCenterInput input, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.CostCenters, ActionCodes.CreateChild, ct: ct);
        var parent = await _repository.GetAsync(parentId, ct) ?? throw new NotFoundAppException();
        if (!parent.IsGroup) throw new ValidationAppException("validation.parent_not_group");

        input.ParentId = parentId;
        Normalize(input);
        Validate(input);
        return await CreateCoreAsync(actor, input, parent.Path + input.Code + "/",
            (short)(parent.Level + 1), ct);
    }

    private async Task<CostCenter> CreateCoreAsync(ActorContext actor, CostCenterInput input,
        string path, short level, CancellationToken ct)
    {
        if (await _repository.GetByCodeAsync(input.Code, ct) is not null)
            throw new ValidationAppException("validation.duplicate_code");

        try
        {
            var id = Guid.CreateVersion7();
            await _repository.CreateAsync(id, input, path, level, actor.UserId, ct);
            var created = await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();
            await _audit.WriteAsync(actor, "cost_centers", id.ToString(), "INSERT", null, created, ct);
            return created;
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }
}
