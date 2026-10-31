using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

/// <summary>
/// إدخال الرسائل إلى الصندوق من منطق العمل (فاتورة/حجز/إشعار خارجي)،
/// مع تحقق مبكر من المستلم والنص قبل أن يراها المعالج الخلفي.
/// </summary>
public sealed class OutboxService
{
    private readonly IMessageOutboxRepository _repository;

    public OutboxService(IMessageOutboxRepository repository) => _repository = repository;

    public async Task<Guid?> EnqueueAsync(OutboxMessageInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Recipient) || input.Recipient.Trim().Length < 3)
            throw new ValidationAppException("validation.outbox_recipient_required");
        if (string.IsNullOrWhiteSpace(input.Body))
            throw new ValidationAppException("validation.outbox_body_required");
        return await _repository.EnqueueAsync(input, ct);
    }
}
