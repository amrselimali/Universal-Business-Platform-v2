using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

/// <summary>متابعات العملاء: إنشاء/إغلاق + توليد تلقائي للحجوزات المتأخرة (4H).</summary>
public sealed class FollowUpService
{
    private readonly IFollowUpRepository _followUps;
    private readonly PermissionService _permissions;
    private readonly AuditService _audit;

    public FollowUpService(IFollowUpRepository followUps, PermissionService permissions,
        AuditService audit)
    {
        _followUps = followUps; _permissions = permissions; _audit = audit;
    }

    public async Task<IReadOnlyList<FollowUp>> ListAsync(ActorContext actor, string? status,
        DateTime? from = null, DateTime? to = null, string? customerSearch = null,
        int limit = 200, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.FollowUps, ActionCodes.View, ct: ct);
        try { return await _followUps.ListAsync(status, from, to, customerSearch, limit, ct); }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }

    /// <summary>
    /// عملاء بلا حجز/موعد/متابعة **من اليوم إلى ما لا نهاية**، مع آخر نشاط مسجّل.
    /// يُفلتر نطاق الأكواد (من كود → إلى كود) والفرع، ويُقصّ على فروع المستخدم.
    /// </summary>
    public async Task<IReadOnlyList<CustomerGapRow>> FindIdleCustomersAsync(ActorContext actor,
        string? codeFrom, string? codeTo, Guid? branchId, int limit = 500,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.FollowUps, ActionCodes.View, ct: ct);
        if (branchId is { } id) BranchScope.Ensure(actor, id);

        var branchIds = actor.IsSuperAdmin
            ? Array.Empty<Guid>()
            : actor.AllowedBranchIds.Distinct().ToArray();

        try
        {
            return await _followUps.FindIdleCustomersAsync(codeFrom, codeTo, branchId, branchIds,
                limit, ct);
        }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }
    }

    public async Task<FollowUp> CreateAsync(ActorContext actor, FollowUpInput input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.FollowUps, ActionCodes.Create, ct: ct);
        var id = Guid.CreateVersion7();
        try { await _followUps.CreateAsync(id, input, actor.UserId, ct); }
        catch (PostgresException ex) { throw DbErrorMapper.Map(ex); }

        var followUp = await _followUps.GetAsync(id, ct) ?? throw new NotFoundAppException();
        await _audit.WriteAsync(actor, "follow_ups", id.ToString(), "INSERT", null, followUp, ct);
        return followUp;
    }

    public async Task<FollowUp> CompleteAsync(ActorContext actor, Guid id, int version,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.FollowUps, ActionCodes.Complete,
            ct: ct);
        var followUp = await _followUps.CompleteAsync(id, version, actor.UserId, ct);
        await _audit.WriteAsync(actor, "follow_ups", id.ToString(), "UPDATE", null,
            new { followUp.Status, followUp.CompletedAt }, ct);
        return followUp;
    }

    public async Task<int> CreateOverdueAsync(ActorContext actor, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.FollowUps, ActionCodes.Create, ct: ct);
        var count = await _followUps.CreateOverdueBookingFollowUpsAsync(actor.UserId, ct);
        await _audit.WriteAsync(actor, "follow_ups", "overdue", "INSERT", null, new { count }, ct);
        return count;
    }
}
