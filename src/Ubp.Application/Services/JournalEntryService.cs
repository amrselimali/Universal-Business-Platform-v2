using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

public sealed partial class JournalEntryService
{
    private readonly IJournalEntryRepository _repository;
    private readonly IJournalPostingRepository _posting;
    private readonly IAccountRepository _accounts;
    private readonly IJournalRepository _journals;
    private readonly PermissionService _permissions;
    private readonly AuditService _audit;

    public JournalEntryService(IJournalEntryRepository repository, IJournalPostingRepository posting,
        IAccountRepository accounts, IJournalRepository journals, PermissionService permissions,
        AuditService audit)
    {
        _repository = repository; _posting = posting; _accounts = accounts; _journals = journals;
        _permissions = permissions; _audit = audit;
    }

    public async Task<PagedResult<JournalEntry>> ListAsync(ActorContext actor, EntryFilter filter,
        PageRequest request, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.JournalEntries, ActionCodes.View, ct: ct);
        if (filter.BranchId is null && await _permissions.GetScopeAsync(actor,
                ScreenCodes.JournalEntries, ActionCodes.View, ct) < PermissionScope.All)
            filter.BranchId = actor.CurrentBranchId;
        return await _repository.ListAsync(filter, request, ct);
    }

    public async Task<JournalEntryDetail> GetAsync(ActorContext actor, Guid id,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.JournalEntry, ActionCodes.View, ct: ct);
        return await _repository.GetDetailAsync(id, ct) ?? throw new NotFoundAppException();
    }

    public async Task<JournalEntryDetail> CreateAsync(ActorContext actor, JournalEntryInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.JournalEntries, ActionCodes.Add, ct: ct);
        await ValidateAsync(input, ct);

        try
        {
            var id = Guid.CreateVersion7();
            await _repository.CreateDraftAsync(id, input, actor.UserId, ct: ct);
            await _audit.WriteAsync(actor, "journal_entries", id.ToString(), "INSERT", null, input, ct);
            return await _repository.GetDetailAsync(id, ct) ?? throw new NotFoundAppException();
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }

    public async Task<JournalEntryDetail> UpdateAsync(ActorContext actor, Guid id, int version,
        JournalEntryInput input, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.JournalEntry, ActionCodes.SaveDraft, ct: ct);
        await ValidateAsync(input, ct);

        try
        {
            var updated = await _repository.UpdateDraftAsync(id, version, input, actor.UserId, ct)
                ?? throw new ConflictException("validation.entry_not_draft");
            await _audit.WriteAsync(actor, "journal_entries", id.ToString(), "UPDATE", null,
                updated, ct);
            return await _repository.GetDetailAsync(id, ct) ?? throw new NotFoundAppException();
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }

    public async Task<JournalEntryDetail> CopyAsync(ActorContext actor, Guid sourceId,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.JournalEntries, ActionCodes.Copy, ct: ct);
        var source = await _repository.GetDetailAsync(sourceId, ct) ?? throw new NotFoundAppException();

        var input = new JournalEntryInput
        {
            JournalId = source.Header.JournalId,
            BranchId = source.Header.BranchId,
            EntryDate = DateTime.Today,
            Reference = source.Header.Reference,
            Description = source.Header.Description,
            Lines = source.Lines.Select(l => new JournalEntryLineInput
            {
                AccountId = l.AccountId, Description = l.Description, Debit = l.Debit, Credit = l.Credit,
                CustomerId = l.CustomerId, SupplierId = l.SupplierId, CostCenterId = l.CostCenterId
            }).ToList()
        };

        return await CreateAsync(actor, input, ct);
    }

    public async Task<JournalEntry> CancelAsync(ActorContext actor, Guid id,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.JournalEntries, ActionCodes.CancelEntry, ct: ct);
        var old = await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();
        var cancelled = await _repository.CancelAsync(id, actor.UserId, ct)
            ?? throw new ValidationAppException("validation.entry_not_draft");
        await _audit.WriteAsync(actor, "journal_entries", id.ToString(), "UPDATE", old, cancelled, ct);
        return cancelled;
    }

    public async Task<MutationOutcome> DeleteAsync(ActorContext actor, Guid id,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.JournalEntry, ActionCodes.SaveDraft, ct: ct);
        var old = await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();
        if (!old.IsDraft) throw new ValidationAppException("validation.entry_immutable");
        if (!await _repository.DeleteDraftAsync(id, ct)) throw new NotFoundAppException();
        await _audit.WriteAsync(actor, "journal_entries", id.ToString(), "DELETE", old, null, ct);
        return new MutationOutcome(true, false, "common.deleted");
    }
}
