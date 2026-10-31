using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

public sealed partial class TreasuryService
{
    /// <summary>
    /// خزينة الرئيسية واحدة على مستوى الشركة: تعيينها يُلغي غيرها. تُستخدم لتجميع الفروع
    /// وللتحويلات بين خزائن الفروع (عبر الحساب الوسيط inter_branch_clearing).
    /// </summary>
    public async Task<Treasury> SetMainTreasuryAsync(ActorContext actor,
        Guid treasuryId, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Treasuries, ActionCodes.SetMain, ct: ct);

        var treasury = (await _treasury.ListTreasuriesAsync(null, ct))
            .FirstOrDefault(t => t.Id == treasuryId) ?? throw new NotFoundAppException();
        BranchScope.Ensure(actor, treasury.BranchId);

        if (!await _treasury.SetMainAsync(treasuryId, actor.UserId, ct))
            throw new ValidationAppException("validation.treasury_inactive");

        await _audit.WriteAsync(actor, "treasuries", treasuryId.ToString(), "UPDATE",
            new { isMain = false }, new { isMain = true }, ct);

        return (await _treasury.ListTreasuriesAsync(null, ct)).First(t => t.Id == treasuryId);
    }

    public Task<Guid?> MainTreasuryIdAsync(CancellationToken ct = default) =>
        _treasury.GetMainAsync(ct);
}
