using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

public sealed class JournalService
{
    private readonly IJournalRepository _repository;
    private readonly PermissionService _permissions;
    private readonly AuditService _audit;

    public JournalService(IJournalRepository repository, PermissionService permissions,
        AuditService audit)
    {
        _repository = repository; _permissions = permissions; _audit = audit;
    }

    public async Task<PagedResult<Journal>> ListAsync(ActorContext actor, JournalFilter filter,
        PageRequest request, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Journals, ActionCodes.View, ct: ct);
        return await _repository.ListAsync(filter, request, ct);
    }

    public async Task<IReadOnlyList<Journal>> OptionsAsync(ActorContext actor, bool includeInactive = false,
        CancellationToken ct = default)
    {
        var allowed = await _permissions.HasAsync(actor, ScreenCodes.Journals, ActionCodes.View, ct)
                   || await _permissions.HasAsync(actor, ScreenCodes.JournalEntry, ActionCodes.View, ct)
                   || await _permissions.HasAsync(actor, ScreenCodes.OpeningBalances, ActionCodes.View, ct);
        if (!allowed) throw new ForbiddenException();
        return await _repository.ListOptionsAsync(includeInactive, ct);
    }

    public async Task<Journal> GetAsync(ActorContext actor, Guid id, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Journals, ActionCodes.View, ct: ct);
        return await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();
    }

    public async Task<Journal> CreateAsync(ActorContext actor, JournalInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Journals, ActionCodes.Add, ct: ct);
        Normalize(input);
        Validate(input);
        if (await _repository.GetByCodeAsync(input.Code, ct) is not null)
            throw new ValidationAppException("validation.duplicate_code");

        try
        {
            var id = Guid.CreateVersion7();
            await _repository.CreateAsync(id, input, actor.UserId, ct);
            var created = await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();
            await _audit.WriteAsync(actor, "journals", id.ToString(), "INSERT", null, created, ct);
            return created;
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }

    public async Task<Journal> UpdateAsync(ActorContext actor, Guid id, int version, JournalInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Journals, ActionCodes.Edit, ct: ct);
        Normalize(input);
        Validate(input);

        var old = await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();
        if (old.IsSystem && old.Code != input.Code)
            throw new ValidationAppException("validation.system_journal");

        try
        {
            var updated = await _repository.UpdateAsync(id, version, input, actor.UserId, ct)
                ?? throw new ConflictException("error.version_conflict");
            await _audit.WriteAsync(actor, "journals", id.ToString(), "UPDATE", old, updated, ct);
            return updated;
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }

    public async Task<Journal> SetActiveAsync(ActorContext actor, Guid id, bool isActive,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Journals, ActionCodes.ToggleActive, ct: ct);
        var old = await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();
        if (old.IsSystem && !isActive)
            throw new ValidationAppException("validation.system_journal");

        var updated = await _repository.SetActiveAsync(id, isActive, actor.UserId, ct)
            ?? throw new NotFoundAppException();
        await _audit.WriteAsync(actor, "journals", id.ToString(), "UPDATE", old, updated, ct);
        return updated;
    }

    public async Task<MutationOutcome> DeleteAsync(ActorContext actor, Guid id,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Journals, ActionCodes.Edit, ct: ct);
        var old = await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();
        if (old.IsSystem) throw new ValidationAppException("validation.system_journal");
        if (old.EntriesCount > 0) throw new ValidationAppException("validation.journal_in_use");

        try
        {
            if (!await _repository.DeleteAsync(id, ct)) throw new NotFoundAppException();
            await _audit.WriteAsync(actor, "journals", id.ToString(), "DELETE", old, null, ct);
            return new MutationOutcome(true, false, "common.deleted");
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }

    private static void Normalize(JournalInput input)
    {
        input.Code = (input.Code ?? "").Trim().ToUpperInvariant();
        input.NameAr = (input.NameAr ?? "").Trim();
        input.NameEn = string.IsNullOrWhiteSpace(input.NameEn) ? null : input.NameEn.Trim();
    }

    private static void Validate(JournalInput input)
    {
        if (input.Code.Length is 0 or > 10)
            throw new ValidationAppException("validation.code_required");
        if (input.NameAr.Length is 0 or > 150)
            throw new ValidationAppException("validation.name_required");
        if (!JournalTypes.All.Contains(input.JournalType))
            throw new ValidationAppException("validation.journal_type");
    }
}
