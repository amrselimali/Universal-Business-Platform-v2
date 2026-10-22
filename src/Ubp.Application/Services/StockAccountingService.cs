using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

public sealed class StockAccountingService
{
    private readonly IStockAccountingRepository _repository;
    private readonly IJournalEntryRepository _entries;
    private readonly IJournalPostingRepository _posting;
    private readonly IAccountingSettingsRepository _settings;
    private readonly PermissionService _permissions;
    private readonly AuditService _audit;

    public StockAccountingService(IStockAccountingRepository repository,
        IJournalEntryRepository entries, IJournalPostingRepository posting,
        IAccountingSettingsRepository settings, PermissionService permissions, AuditService audit)
    {
        _repository = repository; _entries = entries; _posting = posting; _settings = settings;
        _permissions = permissions; _audit = audit;
    }

    public async Task<IReadOnlyList<StockPostingCandidate>> ListAsync(ActorContext actor,
        Guid? branchId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.StockPostingCenter, ActionCodes.View, ct: ct);
        return await _repository.ListCandidatesAsync(branchId, from, to, ct);
    }

    public async Task<StockPostingPreview> PreviewAsync(ActorContext actor, Guid documentId,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.StockPostingCenter, ActionCodes.Preview,
            ct: ct);
        return await _repository.PreviewAsync(documentId, ct);
    }

    public async Task<Guid> GenerateAsync(ActorContext actor, Guid documentId,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.StockPostingCenter,
            ActionCodes.GenerateSingle, ct: ct);
        var entryId = await _repository.GenerateAsync(documentId, actor.UserId, ct);
        await _audit.WriteAsync(actor, "journal_entries", entryId.ToString(), "INSERT", null,
            new { source = EntrySources.StockDocument, documentId }, ct);
        return entryId;
    }

    public async Task<int> GenerateAllAsync(ActorContext actor, Guid? branchId, DateTime from,
        DateTime to, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.StockPostingCenter, ActionCodes.GenerateAll,
            ct: ct);
        var created = await _repository.GenerateAllAsync(branchId, from, to, actor.UserId, ct);
        if (created > 0)
            await _audit.WriteAsync(actor, "journal_entries", "bulk", "INSERT", null,
                new { created, from, to }, ct);
        return created;
    }

    /// <summary>ترحيل تلقائي عند ترحيل مستند مخزون — لا يُفشل ترحيل المخزون لو الإعداد ناقص.</summary>
    public async Task<bool> TryAutoPostAsync(ActorContext actor, Guid documentId,
        CancellationToken ct = default)
    {
        var settings = await _settings.GetAsync(ct);
        if (!settings.AutoPostStockJournals) return false;

        try
        {
            await _repository.GenerateAsync(documentId, actor.UserId, ct);
            return true;
        }
        catch (AppException)
        {
            return false;
        }
        catch (Npgsql.PostgresException)
        {
            return false;
        }
    }

    /// <summary>عكس قيد المستند تلقائياً عند عكس المستند المخزني.</summary>
    public async Task<bool> TryAutoReverseAsync(ActorContext actor, Guid documentId,
        CancellationToken ct = default)
    {
        var settings = await _settings.GetAsync(ct);
        if (!settings.AutoPostStockJournals) return false;

        var entry = await _entries.GetBySourceAsync(EntrySources.StockDocument, documentId, ct);
        if (entry is null || !entry.IsPosted || entry.IsReversed) return false;

        try
        {
            await _posting.ReverseAsync(entry.Id, actor.UserId, DateTime.Today, ct);
            return true;
        }
        catch (AppException)
        {
            return false;
        }
        catch (Npgsql.PostgresException)
        {
            return false;
        }
    }
}
