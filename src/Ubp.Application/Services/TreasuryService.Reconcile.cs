using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

/// <summary>تسوية الحسابات الوسيطة (بطاقات/محافظ/شيكات) إلى الخزينة أو البنك.</summary>
public sealed partial class TreasuryService
{
    public async Task<IReadOnlyList<ClearingBalance>> ListClearingBalancesAsync(
        ActorContext actor, Guid? branchId, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.TreasuryReconciliation,
            ActionCodes.View, ct: ct);
        return await _treasury.ListClearingBalancesAsync(branchId, ct);
    }

    /// <summary>تسوية رصيد وسيط بقيد واحد؛ موجب = من الوسيط إلى الخزينة، سالب = العكس.</summary>
    public async Task<Guid> SettleClearingAsync(ActorContext actor, Guid treasuryId,
        Guid clearingAccountId, decimal amount, DateTime docDate, string? reference,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.TreasuryReconciliation,
            ActionCodes.Post, ct: ct);
        if (treasuryId == Guid.Empty || clearingAccountId == Guid.Empty)
            throw new ValidationAppException("validation.required_fields");
        if (amount == 0)
            throw new ValidationAppException("validation.settlement_amount");

        Guid entryId;
        try
        {
            entryId = await _treasury.SettleClearingAsync(treasuryId, clearingAccountId, amount,
                docDate, reference, actor.UserId, ct);
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }

        await _audit.WriteAsync(actor, "journal_entries", entryId.ToString(), "INSERT", null,
            new { treasuryId, clearingAccountId, amount, docDate, reference }, ct);
        return entryId;
    }
}
