using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

public sealed partial class WarehouseService
{
    public async Task<MutationOutcome> DeleteAsync(ActorContext actor, Guid id,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Warehouses, ActionCodes.Delete, ct: ct);
        var old = await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();
        EnsureAccess(actor, old);
        var (users, branches) = await _repository.CountLinksAsync(id, ct);
        if (users > 0 || branches > 0)
        {
            var deactivated = await _repository.SetActiveAsync(id, false, ct);
            await _audit.WriteAsync(actor, "warehouses", id.ToString(), "UPDATE", old, deactivated, ct);
            return new MutationOutcome(false, true, "warehouse.deactivated");
        }

        try
        {
            if (!await _repository.DeleteAsync(id, ct)) throw new NotFoundAppException();
            await _audit.WriteAsync(actor, "warehouses", id.ToString(), "DELETE", old, null, ct);
            return new MutationOutcome(true, false, "warehouse.deleted");
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            var deactivated = await _repository.SetActiveAsync(id, false, ct);
            await _audit.WriteAsync(actor, "warehouses", id.ToString(), "UPDATE", old, deactivated, ct);
            return new MutationOutcome(false, true, "warehouse.deactivated");
        }
    }

    public async Task<Warehouse> SetActiveAsync(ActorContext actor, Guid id, bool isActive,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Warehouses, ActionCodes.ToggleActive, ct: ct);
        var old = await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();
        EnsureAccess(actor, old);
        var updated = await _repository.SetActiveAsync(id, isActive, ct)
            ?? throw new NotFoundAppException();
        await _audit.WriteAsync(actor, "warehouses", id.ToString(), "UPDATE", old, updated, ct);
        return updated;
    }

    public async Task<Warehouse> SetBranchesAsync(ActorContext actor, Guid id, List<Guid> branchIds,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Warehouses, ActionCodes.LinkBranches, ct: ct);
        var old = await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();
        EnsureAccess(actor, old);
        await ValidateBranchesAsync(actor, branchIds.Distinct().ToList(), ct);
        try
        {
            var updated = await _repository.UpdateAsync(id, old.Version, new WarehouseInput
            {
                Code = old.Code, NameAr = old.NameAr, NameEn = old.NameEn, IsActive = old.IsActive,
                CustomFields = old.CustomFields, BranchIds = branchIds
            }, ct) ?? throw new ConflictException("error.version_conflict");
            await _audit.WriteAsync(actor, "warehouses", id.ToString(), "UPDATE", old, updated, ct);
            return updated;
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }
}
