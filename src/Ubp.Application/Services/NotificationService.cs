using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

/// <summary>
/// الإشعارات الداخلية: قراءة/تعليم كمقروء/مسح بصلاحيات صريحة، وإرسال داخلي موثوق
/// (تستخدمه الخدمات الأخرى مثل إغلاق الفترة) يبثّ الإشعار للدوائر المفتوحة فوراً.
/// </summary>
public sealed class NotificationService
{
    private readonly INotificationRepository _repository;
    private readonly PermissionService _permissions;
    private readonly NotificationBroadcaster _broadcaster;

    public NotificationService(INotificationRepository repository, PermissionService permissions,
        NotificationBroadcaster broadcaster)
    {
        _repository = repository; _permissions = permissions; _broadcaster = broadcaster;
    }

    public async Task<PagedResult<Notification>> ListAsync(ActorContext actor, PageRequest request,
        bool unreadOnly, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Notifications, ActionCodes.View, ct: ct);
        return await _repository.ListAsync(actor.UserId, request, unreadOnly, ct);
    }

    /// <summary>عدّاد الجرس: يرجع صفراً لمن لا يملك شاشة الإشعارات بدل رمي استثناء في الترويسة.</summary>
    public async Task<int> UnreadCountAsync(ActorContext actor, CancellationToken ct = default)
    {
        if (!await _permissions.HasAsync(actor, ScreenCodes.Notifications, ActionCodes.View, ct: ct))
            return 0;
        return await _repository.CountUnreadAsync(actor.UserId, ct);
    }

    public async Task MarkReadAsync(ActorContext actor, Guid id, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Notifications, ActionCodes.MarkAsRead, ct: ct);
        if (!await _repository.MarkReadAsync(id, actor.UserId, ct)) throw new NotFoundAppException();
    }

    public async Task MarkAllReadAsync(ActorContext actor, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Notifications, ActionCodes.MarkAsRead, ct: ct);
        await _repository.MarkAllReadAsync(actor.UserId, ct);
    }

    public async Task ClearAsync(ActorContext actor, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Notifications, ActionCodes.ClearAll, ct: ct);
        await _repository.ClearAsync(actor.UserId, ct);
    }

    /// <summary>إرسال داخلي موثوق: لمستخدم واحد أو لدور (واحد فقط)، مع بثّ فوري.</summary>
    public async Task<Notification> SendAsync(NotificationInput input,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.TitleKey))
            throw new ValidationAppException("validation.notification_title_required");
        if (input.UserId.HasValue == input.RoleId.HasValue)
            throw new ValidationAppException("validation.notification_target_invalid");

        var id = await _repository.CreateAsync(input, ct);
        var notification = new Notification
        {
            Id = id, UserId = input.UserId, RoleId = input.RoleId, TitleKey = input.TitleKey,
            Body = input.Body, LinkUrl = input.LinkUrl,
            Severity = NotificationSeverities.Normalize(input.Severity),
            CreatedAt = DateTimeOffset.UtcNow
        };

        var targets = input.UserId.HasValue
            ? [input.UserId.Value]
            : await _repository.ListRoleUserIdsAsync(input.RoleId!.Value, ct);
        foreach (var userId in targets)
            await _broadcaster.PublishAsync(userId, notification);
        return notification;
    }
}
