using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

/// <summary>
/// إدخال الرسائل إلى الصندوق من منطق العمل (فاتورة/حجز/إشعار خارجي)،
/// مع تحقق مبكر من المستلم والنص قبل أن يراها المعالج الخلفي.
/// ويوفّر عرض الصندوق وإعادة المحاولة/الإلغاء لشاشة صندوق الرسائل.
/// </summary>
public sealed class OutboxService
{
    private readonly IMessageOutboxRepository _repository;
    private readonly PermissionService _permissions;

    public OutboxService(IMessageOutboxRepository repository, PermissionService permissions)
    {
        _repository = repository; _permissions = permissions;
    }

    public async Task<Guid?> EnqueueAsync(OutboxMessageInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Recipient) || input.Recipient.Trim().Length < 3)
            throw new ValidationAppException("validation.outbox_recipient_required");
        if (string.IsNullOrWhiteSpace(input.Body))
            throw new ValidationAppException("validation.outbox_body_required");
        return await _repository.EnqueueAsync(input, ct);
    }

    public async Task<IReadOnlyList<OutboxMessage>> ListAsync(ActorContext actor,
        string? status = null, string? channel = null, int limit = 200,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Notifications, ActionCodes.View, ct: ct);
        return await _repository.ListAsync(string.IsNullOrWhiteSpace(status) ? null : status.Trim(),
            string.IsNullOrWhiteSpace(channel) ? null : MessageChannels.Normalize(channel),
            limit, ct);
    }

    public async Task<bool> RetryAsync(ActorContext actor, Guid id, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Notifications,
            ActionCodes.RetryOutbox, ct: ct);
        return await _repository.RetryAsync(id, ct);
    }

    public async Task<bool> CancelAsync(ActorContext actor, Guid id, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Notifications,
            ActionCodes.CancelOutbox, ct: ct);
        return await _repository.CancelPendingAsync(id, ct);
    }
}
