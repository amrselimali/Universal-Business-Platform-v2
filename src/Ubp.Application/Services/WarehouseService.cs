using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

public sealed partial class WarehouseService
{
    private readonly IWarehouseRepository _repository;
    private readonly IBranchRepository _branches;
    private readonly PermissionService _permissions;
    private readonly CustomFieldService _customFields;
    private readonly AuditService _audit;

    public WarehouseService(IWarehouseRepository repository, IBranchRepository branches,
        PermissionService permissions, CustomFieldService customFields, AuditService audit)
    {
        _repository = repository; _branches = branches; _permissions = permissions;
        _customFields = customFields; _audit = audit;
    }

    public async Task<PagedResult<Warehouse>> ListAsync(ActorContext actor, PageRequest request,
        CancellationToken ct = default)
    {
        var scope = await _permissions.GetScopeAsync(actor, ScreenCodes.Warehouses, ActionCodes.View, ct);
        if (scope < 0) throw new ForbiddenException();
        return await _repository.ListAsync(request, scope, actor.UserId,
            actor.AllowedBranchIds, actor.IsAdmin, ct);
    }

    public async Task<IReadOnlyList<Warehouse>> OptionsAsync(ActorContext actor,
        CancellationToken ct = default)
    {
        var scope = await _permissions.GetScopeAsync(actor, ScreenCodes.Warehouses, ActionCodes.View, ct);
        if (scope < 0) return Array.Empty<Warehouse>();
        return await _repository.ListOptionsAsync(scope, actor.UserId, actor.AllowedBranchIds,
            actor.IsAdmin, ct);
    }

    public async Task<Warehouse?> GetAsync(ActorContext actor, Guid id, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Warehouses, ActionCodes.View, ct: ct);
        var warehouse = await _repository.GetAsync(id, ct);
        if (warehouse is null) return null;
        EnsureAccess(actor, warehouse);
        return warehouse;
    }

    public async Task<Warehouse> CreateAsync(ActorContext actor, WarehouseInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Warehouses, ActionCodes.Add, ct: ct);
        Normalize(input);
        Validate(input);
        await ValidateBranchesAsync(actor, input.BranchIds, ct);
        input.CustomFields = await _customFields.ValidateValuesAsync("warehouses", input.CustomFields, ct);
        try
        {
            var created = await _repository.CreateAsync(input, ct);
            await _audit.WriteAsync(actor, "warehouses", created.Id.ToString(), "INSERT", null, created, ct);
            return created;
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }

    public async Task<Warehouse> UpdateAsync(ActorContext actor, Guid id, int version,
        WarehouseInput input, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Warehouses, ActionCodes.Edit, ct: ct);
        var old = await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();
        EnsureAccess(actor, old);
        Normalize(input);
        Validate(input);
        if (!old.BranchIds.OrderBy(x => x).SequenceEqual(input.BranchIds.Distinct().OrderBy(x => x)))
            await _permissions.RequireAsync(actor, ScreenCodes.Warehouses, ActionCodes.LinkBranches, ct: ct);
        await ValidateBranchesAsync(actor, input.BranchIds, ct);
        input.CustomFields = await _customFields.ValidateValuesAsync("warehouses", input.CustomFields, ct);
        try
        {
            var updated = await _repository.UpdateAsync(id, version, input, ct)
                ?? throw new ConflictException("error.version_conflict");
            await _audit.WriteAsync(actor, "warehouses", id.ToString(), "UPDATE", old, updated, ct);
            return updated;
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }

    private static void Normalize(WarehouseInput input)
    {
        input.Code = (input.Code ?? "").Trim().ToUpperInvariant();
        input.NameAr = (input.NameAr ?? "").Trim();
        input.NameEn = string.IsNullOrWhiteSpace(input.NameEn) ? null : input.NameEn.Trim();
        input.BranchIds = input.BranchIds.Distinct().ToList();
    }

    private static void Validate(WarehouseInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Code) || input.Code.Length > 30)
            throw new ValidationAppException("validation.code_required");
        if (string.IsNullOrWhiteSpace(input.NameAr) || input.NameAr.Length > 255)
            throw new ValidationAppException("validation.name_required");
    }

    private async Task ValidateBranchesAsync(ActorContext actor, IReadOnlyList<Guid> branchIds,
        CancellationToken ct)
    {
        if (!actor.IsAdmin && branchIds.Any(id => !actor.CanAccessBranch(id)))
            throw new ForbiddenException("error.branch_not_allowed");
        foreach (var branchId in branchIds)
        {
            if (await _branches.GetAsync(branchId, ct) is null)
                throw new ValidationAppException("validation.branch_not_found");
        }
    }

    private static void EnsureAccess(ActorContext actor, Warehouse warehouse)
    {
        if (actor.IsAdmin) return;
        if (!warehouse.BranchIds.Any(actor.CanAccessBranch))
            throw new ForbiddenException("error.branch_not_allowed");
    }
}
