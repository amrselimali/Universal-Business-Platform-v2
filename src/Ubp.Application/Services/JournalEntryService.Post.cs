using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;

namespace Ubp.Application.Services;

public sealed partial class JournalEntryService
{
    /// <summary>الترحيل: التوازن يُفحص هنا وفي قاعدة البيانات، والأرصدة تُحدّث ذرياً.</summary>
    public async Task<JournalEntry> PostAsync(ActorContext actor, Guid id,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.JournalEntry, ActionCodes.Post, ct: ct);
        var detail = await _repository.GetDetailAsync(id, ct) ?? throw new NotFoundAppException();
        if (!detail.Header.IsDraft) throw new ValidationAppException("validation.entry_not_draft");
        if (detail.Lines.Count < 2) throw new ValidationAppException("validation.entry_needs_two_lines");
        if (!detail.IsBalanced) throw new ValidationAppException("validation.entry_not_balanced");

        try
        {
            await _posting.PostAsync(id, actor.UserId, ct);
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }

        var posted = await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();
        await _audit.WriteAsync(actor, "journal_entries", id.ToString(), "UPDATE",
            detail.Header, posted, ct);
        return posted;
    }

    public async Task<JournalEntry> ReverseAsync(ActorContext actor, Guid id, DateTime reversalDate,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.JournalEntry, ActionCodes.Reverse, ct: ct);
        var old = await _repository.GetAsync(id, ct) ?? throw new NotFoundAppException();
        if (!old.IsPosted) throw new ValidationAppException("validation.entry_not_posted");
        if (old.IsReversed) throw new ValidationAppException("validation.entry_already_reversed");
        if (old.ReversalOfId is not null)
            throw new ValidationAppException("validation.entry_is_reversal");

        try
        {
            var newId = await _posting.ReverseAsync(id, actor.UserId, reversalDate.Date, ct);
            var reversal = await _repository.GetAsync(newId, ct) ?? throw new NotFoundAppException();
            await _audit.WriteAsync(actor, "journal_entries", newId.ToString(), "INSERT", null,
                reversal, ct);
            var refreshed = await _repository.GetAsync(id, ct) ?? old;
            await _audit.WriteAsync(actor, "journal_entries", id.ToString(), "UPDATE", old,
                refreshed, ct);
            return reversal;
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }
}
