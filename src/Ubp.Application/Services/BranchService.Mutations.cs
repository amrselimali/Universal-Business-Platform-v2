using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

public sealed partial class BranchService
{
    public async Task<MutationOutcome> DeleteAsync(ActorContext actor, Guid id,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Branches, ActionCodes.Delete, ct: ct);
        if (!actor.CanAccessBranch(id)) throw new ForbiddenException();
        var old = await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();
        var (users, warehouses) = await _repository.CountLinksAsync(id, ct);
        if (users > 0 || warehouses > 0 || id == actor.CurrentBranchId)
        {
            var deactivated = await _repository.SetActiveAsync(id, false, ct);
            await _audit.WriteAsync(actor, "branches", id.ToString(), "UPDATE", old, deactivated, ct);
            return new MutationOutcome(false, true, "branch.deactivated");
        }

        try
        {
            if (!await _repository.DeleteAsync(id, ct)) throw new NotFoundAppException();
            await _audit.WriteAsync(actor, "branches", id.ToString(), "DELETE", old, null, ct);
            return new MutationOutcome(true, false, "branch.deleted");
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            var deactivated = await _repository.SetActiveAsync(id, false, ct);
            await _audit.WriteAsync(actor, "branches", id.ToString(), "UPDATE", old, deactivated, ct);
            return new MutationOutcome(false, true, "branch.deactivated");
        }
    }

    public async Task<Branch> SetActiveAsync(ActorContext actor, Guid id, bool isActive,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Branches, ActionCodes.ToggleActive, ct: ct);
        if (!actor.CanAccessBranch(id)) throw new ForbiddenException();
        var old = await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();
        var updated = await _repository.SetActiveAsync(id, isActive, ct)
            ?? throw new NotFoundAppException();
        await _audit.WriteAsync(actor, "branches", id.ToString(), "UPDATE", old, updated, ct);
        return updated;
    }

    private static void Normalize(BranchInput input)
    {
        input.Code = (input.Code ?? "").Trim().ToUpperInvariant();
        input.NameAr = (input.NameAr ?? "").Trim();
        input.NameEn = string.IsNullOrWhiteSpace(input.NameEn) ? null : input.NameEn.Trim();
    }

    private static void Validate(BranchInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Code) || input.Code.Length > 30)
            throw new ValidationAppException("validation.code_required");
        if (string.IsNullOrWhiteSpace(input.NameAr) || input.NameAr.Length > 255)
            throw new ValidationAppException("validation.name_required");
    }
}
