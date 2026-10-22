using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

public sealed partial class OpeningBalanceService
{
    private readonly IJournalEntryRepository _entries;
    private readonly IJournalPostingRepository _posting;
    private readonly IJournalRepository _journals;
    private readonly IAccountRepository _accounts;
    private readonly ICostCenterRepository _costCenters;
    private readonly IPartnerRepository _partners;
    private readonly JournalEntryService _entryService;
    private readonly PermissionService _permissions;
    private readonly AuditService _audit;

    public OpeningBalanceService(IJournalEntryRepository entries, IJournalPostingRepository posting,
        IJournalRepository journals, IAccountRepository accounts, ICostCenterRepository costCenters,
        IPartnerRepository partners, JournalEntryService entryService, PermissionService permissions,
        AuditService audit)
    {
        _entries = entries; _posting = posting; _journals = journals; _accounts = accounts;
        _costCenters = costCenters; _partners = partners; _entryService = entryService;
        _permissions = permissions; _audit = audit;
    }

    /// <summary>مسودة الأرصدة الافتتاحية الحالية للفرع (قيد مصدره opening وحالته draft).</summary>
    public async Task<JournalEntryDetail?> GetDraftAsync(ActorContext actor, Guid branchId,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.OpeningBalances, ActionCodes.View, ct: ct);
        var draft = await FindDraftAsync(branchId, ct);
        return draft is null ? null : await _entries.GetDetailAsync(draft.Id, ct);
    }

    public async Task<JournalEntryDetail> SaveDraftAsync(ActorContext actor,
        OpeningBalanceInput input, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.OpeningBalances, ActionCodes.SaveDraft, ct: ct);
        if (input.Lines.Count == 0)
            throw new ValidationAppException("validation.entry_needs_two_lines");

        var journalId = input.JournalId;
        if (journalId == Guid.Empty)
            journalId = (await _journals.GetByTypeAsync(JournalTypes.Opening, ct)
                         ?? throw new ValidationAppException("validation.opening_journal_missing")).Id;

        var request = new JournalEntryInput
        {
            JournalId = journalId,
            BranchId = input.BranchId,
            EntryDate = input.EntryDate,
            Reference = "OPENING",
            Description = string.IsNullOrWhiteSpace(input.Description)
                ? "الأرصدة الافتتاحية" : input.Description,
            Lines = input.Lines
        };

        await _entryService.ValidateAsync(request, ct);

        var existing = await FindDraftAsync(input.BranchId, ct);
        if (existing is null)
        {
            var id = Guid.CreateVersion7();
            await _entries.CreateDraftAsync(id, request, actor.UserId, EntrySources.Opening, null, ct);
            await _audit.WriteAsync(actor, "journal_entries", id.ToString(), "INSERT", null, request, ct);
            return await _entries.GetDetailAsync(id, ct) ?? throw new NotFoundAppException();
        }

        var updated = await _entries.UpdateDraftAsync(existing.Id, existing.Version, request,
            actor.UserId, ct) ?? throw new ConflictException("error.version_conflict");
        await _audit.WriteAsync(actor, "journal_entries", existing.Id.ToString(), "UPDATE",
            existing, updated, ct);
        return await _entries.GetDetailAsync(existing.Id, ct) ?? throw new NotFoundAppException();
    }

    public async Task<JournalEntry> PostAsync(ActorContext actor, Guid entryId,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.OpeningBalances, ActionCodes.Post, ct: ct);
        var detail = await _entries.GetDetailAsync(entryId, ct) ?? throw new NotFoundAppException();
        if (!detail.Header.IsDraft) throw new ValidationAppException("validation.entry_not_draft");
        if (!detail.IsBalanced) throw new ValidationAppException("validation.entry_not_balanced");

        try
        {
            await _posting.PostAsync(entryId, actor.UserId, ct);
        }
        catch (Npgsql.PostgresException ex) { throw DbErrorMapper.Map(ex); }

        var posted = await _entries.GetAsync(entryId, ct) ?? throw new NotFoundAppException();
        await _audit.WriteAsync(actor, "journal_entries", entryId.ToString(), "UPDATE",
            detail.Header, posted, ct);
        return posted;
    }

    /// <summary>موازنة الأرصدة الافتتاحية على حساب حقوق الملكية (رأس المال الافتراضي).</summary>
    public async Task AutoBalanceEquityAsync(ActorContext actor, IList<JournalEntryLineInput> lines,
        Guid? equityAccountId, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.OpeningBalances,
            ActionCodes.AutoBalanceEquity, ct: ct);

        var difference = lines.Sum(l => l.Debit) - lines.Sum(l => l.Credit);
        if (difference == 0) return;

        var target = equityAccountId;
        if (target is null)
        {
            var equity = await _accounts.FindByTypeAsync(AccountTypes.Equity, ct)
                ?? throw new ValidationAppException("validation.equity_account_missing");
            target = equity.Id;
        }

        lines.Add(new JournalEntryLineInput
        {
            AccountId = target.Value,
            Description = "موازنة الأرصدة الافتتاحية",
            Debit = difference < 0 ? -difference : 0,
            Credit = difference > 0 ? difference : 0
        });
    }

    private async Task<JournalEntry?> FindDraftAsync(Guid branchId, CancellationToken ct)
    {
        var page = await _entries.ListAsync(new EntryFilter
        {
            SourceType = EntrySources.Opening, Status = EntryStatuses.Draft, BranchId = branchId
        }, new PageRequest { Page = 1, PageSize = 1 }, ct);
        return page.Items.FirstOrDefault();
    }
}
