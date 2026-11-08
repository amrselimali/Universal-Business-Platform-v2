using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;

namespace Ubp.Application.Services;

/// <summary>تكويد الخزائن: تعديل/أرشفة/متابعة الشفت/عمولة مزود الخدمة — 0093.</summary>
public sealed partial class TreasuryService
{
    /// <summary>تعديل خزينة مسجّلة (كود/اسم/نوع/حساب/عمولات/متابعة الشفت) — 0093.</summary>
    public async Task UpdateTreasuryAsync(ActorContext actor, Guid treasuryId, TreasuryInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Treasuries, ActionCodes.Edit, ct: ct);
        if (string.IsNullOrWhiteSpace(input.Code) || string.IsNullOrWhiteSpace(input.NameAr) ||
            input.AccountId == Guid.Empty)
            throw new ValidationAppException("validation.required_fields");
        if (!IsTreasuryType(input.TreasuryType))
            throw new ValidationAppException("validation.treasury_type");
        if (input.OpeningBalance < 0)
            throw new ValidationAppException("validation.opening_balance");
        if (input.CollectionFeePercent is < 0 or > 100 || input.ProviderFeePercent is < 0 or > 100)
            throw new ValidationAppException("validation.payment_fee");

        var postable = await _accounts.ListPostableAsync(ct);
        if (postable.All(a => a.Id != input.AccountId))
            throw new ValidationAppException("validation.account_not_postable");

        try
        {
            if (!await _treasury.UpdateTreasuryAsync(treasuryId, input, actor.UserId, ct))
                throw new NotFoundAppException();
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }

        await _audit.WriteAsync(actor, "treasuries", treasuryId.ToString(), "UPDATE", null, input,
            ct);
    }

    /// <summary>أرشفة خزينة (إخفاء من الشاشات مع الاحتفاظ بحركاتها) — 0093.</summary>
    public async Task ArchiveTreasuryAsync(ActorContext actor, Guid treasuryId,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Treasuries, ActionCodes.Delete, ct: ct);
        if (!await _treasury.SetTreasuryActiveAsync(treasuryId, false, actor.UserId, ct))
            throw new NotFoundAppException();
        await _audit.WriteAsync(actor, "treasuries", treasuryId.ToString(), "DELETE", null,
            new { is_active = false, archived = true }, ct);
    }

    /// <summary>استرجاع خزينة مؤرشفة — 0093.</summary>
    public async Task RestoreTreasuryAsync(ActorContext actor, Guid treasuryId,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Treasuries, ActionCodes.Edit, ct: ct);
        if (!await _treasury.SetTreasuryActiveAsync(treasuryId, true, actor.UserId, ct))
            throw new NotFoundAppException();
        await _audit.WriteAsync(actor, "treasuries", treasuryId.ToString(), "UPDATE", null,
            new { is_active = true, archived = false }, ct);
    }

    /// <summary>تشغيل/إيقاف متابعة الخزينة في الشفت — 0093.</summary>
    public async Task SetTreasuryFollowInShiftAsync(ActorContext actor, Guid treasuryId,
        bool followInShift, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Treasuries, ActionCodes.ManageMethods,
            ct: ct);
        if (!await _treasury.SetTreasuryFollowInShiftAsync(treasuryId, followInShift, ct))
            throw new NotFoundAppException();
        await _audit.WriteAsync(actor, "treasuries", treasuryId.ToString(), "UPDATE", null,
            new { follow_in_shift = followInShift }, ct);
    }

    /// <summary>عمولة التحصيل المحمَّلة من مزود الخدمة للخزينة — 0093.</summary>
    public async Task SetProviderFeeAsync(ActorContext actor, Guid treasuryId, decimal percent,
        decimal amount, Guid? accountId, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Treasuries, ActionCodes.ManageMethods,
            ct: ct);
        if (percent is < 0 or > 100 || amount < 0)
            throw new ValidationAppException("validation.payment_fee");
        if (accountId is { } id)
        {
            var postable = await _accounts.ListPostableAsync(ct);
            if (postable.All(a => a.Id != id))
                throw new ValidationAppException("validation.account_not_postable");
        }
        if (!await _treasury.SetProviderFeeAsync(treasuryId, percent, amount, accountId,
                actor.UserId, ct))
            throw new NotFoundAppException();
        await _audit.WriteAsync(actor, "treasuries", treasuryId.ToString(), "UPDATE", null,
            new { provider_fee_percent = percent, provider_fee_amount = amount,
                provider_fee_account_id = accountId }, ct);
    }

}
