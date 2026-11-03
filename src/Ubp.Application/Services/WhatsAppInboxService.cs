using Ubp.Core.Models;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

/// <summary>الرسائل الواردة من واتساب (Webhook) للعرض في شاشة الإشعارات.</summary>
public sealed class WhatsAppInboxService
{
    private readonly IWhatsAppInboundRepository _inbound;
    private readonly PermissionService _permissions;

    public WhatsAppInboxService(IWhatsAppInboundRepository inbound, PermissionService permissions)
    {
        _inbound = inbound; _permissions = permissions;
    }

    public async Task<IReadOnlyList<WhatsAppInboundMessage>> ListAsync(ActorContext actor,
        int limit = 200, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Notifications, ActionCodes.View, ct: ct);
        return await _inbound.ListAsync(limit, ct);
    }
}
