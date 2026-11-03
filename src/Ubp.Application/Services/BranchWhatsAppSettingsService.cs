using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

/// <summary>
/// إعدادات واتساب لكل فرع: إدارة مستقلة داخل نطاق الفرع، والتوكن يُحفظ في قاعدة البيانات
/// (لا يُوضع في الكود)، وعند تركه فارغًا عند التعديل يبقى التوكن الحالي.
/// </summary>
public sealed class BranchWhatsAppSettingsService
{
    private readonly IBranchWhatsAppSettingsRepository _repository;
    private readonly PermissionService _permissions;
    private readonly AuditService _audit;

    public BranchWhatsAppSettingsService(IBranchWhatsAppSettingsRepository repository,
        PermissionService permissions, AuditService audit)
    {
        _repository = repository; _permissions = permissions; _audit = audit;
    }

    public async Task<BranchWhatsAppSettings> GetAsync(ActorContext actor, Guid branchId,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Branches, ActionCodes.ManageDefaults,
            ct: ct);
        BranchScope.Ensure(actor, branchId);
        return await _repository.GetAsync(branchId, ct) ?? new BranchWhatsAppSettings
        { BranchId = branchId };
    }

    public async Task<BranchWhatsAppSettings> SaveAsync(ActorContext actor,
        BranchWhatsAppSettingsInput input, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Branches, ActionCodes.ManageDefaults,
            ct: ct);
        BranchScope.Ensure(actor, input.BranchId);
        if (!Uri.TryCreate(input.ApiUrl, UriKind.Absolute, out _))
            throw new ValidationAppException("validation.whatsapp_api_url_invalid");

        if (string.IsNullOrWhiteSpace(input.AccessToken))
            input.AccessToken = (await _repository.GetAsync(input.BranchId, ct))?.AccessToken;
        if (input.IsEnabled && (string.IsNullOrWhiteSpace(input.PhoneNumberId) ||
                                string.IsNullOrWhiteSpace(input.AccessToken)))
            throw new ValidationAppException("validation.whatsapp_credentials_required");

        var saved = await _repository.UpsertAsync(input, actor.UserId, ct);
        await _audit.WriteAsync(actor, "branch_whatsapp_settings", input.BranchId.ToString(),
            "UPDATE", null, new
            {
                input.IsEnabled, input.ApiUrl, input.PhoneNumberId, input.MessagingAccountId
            }, ct);
        return saved;
    }
}
