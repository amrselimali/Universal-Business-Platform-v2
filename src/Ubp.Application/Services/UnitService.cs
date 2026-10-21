using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

public sealed class UnitService
{
    private readonly IUnitRepository _repository;
    private readonly PermissionService _permissions;
    private readonly AuditService _audit;

    public UnitService(IUnitRepository repository, PermissionService permissions, AuditService audit)
    {
        _repository = repository; _permissions = permissions; _audit = audit;
    }

    public async Task<PagedResult<Unit>> ListAsync(ActorContext actor, PageRequest request,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Units, ActionCodes.View, ct: ct);
        return await _repository.ListAsync(request, ct);
    }

    public async Task<IReadOnlyList<Unit>> OptionsAsync(ActorContext actor, CancellationToken ct = default)
    {
        var allowed = await _permissions.HasAsync(actor, ScreenCodes.Units, ActionCodes.View, ct)
                   || await _permissions.HasAsync(actor, ScreenCodes.Items, ActionCodes.View, ct);
        if (!allowed) throw new ForbiddenException();
        return await _repository.ListOptionsAsync(ct);
    }

    public async Task<Unit> CreateAsync(ActorContext actor, UnitInput input, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Units, ActionCodes.Add, ct: ct);
        Normalize(input); Validate(input);
        try
        {
            var created = await _repository.CreateAsync(input, ct);
            await _audit.WriteAsync(actor, "units", created.Id.ToString(), "INSERT", null, created, ct);
            return created;
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }

    public async Task<Unit> UpdateAsync(ActorContext actor, Guid id, int version, UnitInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Units, ActionCodes.Edit, ct: ct);
        Normalize(input); Validate(input);
        var old = await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();
        try
        {
            var updated = await _repository.UpdateAsync(id, version, input, ct)
                ?? throw new ConflictException("error.version_conflict");
            await _audit.WriteAsync(actor, "units", id.ToString(), "UPDATE", old, updated, ct);
            return updated;
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }

    public async Task<MutationOutcome> DeleteAsync(ActorContext actor, Guid id, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Units, ActionCodes.Delete, ct: ct);
        var old = await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();
        if (await _repository.CountUsageAsync(id, ct) > 0)
            throw new ValidationAppException("validation.unit_in_use");

        try
        {
            if (!await _repository.DeleteAsync(id, ct)) throw new NotFoundAppException();
            await _audit.WriteAsync(actor, "units", id.ToString(), "DELETE", old, null, ct);
            return new MutationOutcome(true, false, "common.deleted");
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }

    public async Task<Unit> SetActiveAsync(ActorContext actor, Guid id, bool isActive,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Units, ActionCodes.ToggleActive, ct: ct);
        var old = await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();
        var updated = await _repository.SetActiveAsync(id, isActive, ct) ?? throw new NotFoundAppException();
        await _audit.WriteAsync(actor, "units", id.ToString(), "UPDATE", old, updated, ct);
        return updated;
    }

    private static void Normalize(UnitInput input)
    {
        input.Code = (input.Code ?? "").Trim().ToUpperInvariant();
        input.NameAr = (input.NameAr ?? "").Trim();
        input.NameEn = string.IsNullOrWhiteSpace(input.NameEn) ? null : input.NameEn.Trim();
    }

    private static void Validate(UnitInput input)
    {
        if (input.Code.Length == 0 || input.Code.Length > 30)
            throw new ValidationAppException("validation.code_required");
        if (input.NameAr.Length == 0 || input.NameAr.Length > 255)
            throw new ValidationAppException("validation.name_required");
        if (input.Decimals is < 0 or > 6)
            throw new ValidationAppException("validation.decimals_range");
    }
}
