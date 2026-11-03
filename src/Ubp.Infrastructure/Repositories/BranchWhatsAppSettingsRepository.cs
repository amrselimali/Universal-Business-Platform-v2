using Dapper;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IBranchWhatsAppSettingsRepository
{
    Task<BranchWhatsAppSettings?> GetAsync(Guid branchId, CancellationToken ct = default);
    Task<BranchWhatsAppSettings> UpsertAsync(BranchWhatsAppSettingsInput input, Guid userId,
        CancellationToken ct = default);
}

/// <summary>إعدادات واتساب لكل فرع: مصدر واحد في قاعدة البيانات (لا توكّنات في الكود).</summary>
public sealed class BranchWhatsAppSettingsRepository : IBranchWhatsAppSettingsRepository
{
    private readonly IDbConnectionFactory _factory;
    public BranchWhatsAppSettingsRepository(IDbConnectionFactory factory) => _factory = factory;

    private const string Select = """
        SELECT branch_id AS BranchId, is_enabled AS IsEnabled, api_url AS ApiUrl,
               phone_number_id AS PhoneNumberId, messaging_account_id AS MessagingAccountId,
               access_token AS AccessToken, webhook_verify_token AS WebhookVerifyToken,
               app_secret AS AppSecret, updated_at AS UpdatedAt
        FROM branch_whatsapp_settings WHERE branch_id = @branchId
        """;

    public async Task<BranchWhatsAppSettings?> GetAsync(Guid branchId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<BranchWhatsAppSettings>(
            new CommandDefinition(Select, new { branchId }, cancellationToken: ct));
    }

    public async Task<BranchWhatsAppSettings> UpsertAsync(BranchWhatsAppSettingsInput input,
        Guid userId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO branch_whatsapp_settings
                (branch_id, is_enabled, api_url, phone_number_id, messaging_account_id,
                 access_token, webhook_verify_token, app_secret, updated_by)
            VALUES (@BranchId, @IsEnabled, @ApiUrl, @PhoneNumberId, @MessagingAccountId,
                    @AccessToken, @WebhookVerifyToken, @AppSecret, @userId)
            ON CONFLICT (branch_id) DO UPDATE SET
                is_enabled = EXCLUDED.is_enabled,
                api_url = EXCLUDED.api_url,
                phone_number_id = EXCLUDED.phone_number_id,
                messaging_account_id = EXCLUDED.messaging_account_id,
                access_token = EXCLUDED.access_token,
                webhook_verify_token = EXCLUDED.webhook_verify_token,
                app_secret = EXCLUDED.app_secret,
                updated_by = @userId,
                updated_at = now()
            """, new { input.BranchId, input.IsEnabled, input.ApiUrl, input.PhoneNumberId,
                input.MessagingAccountId, input.AccessToken, input.WebhookVerifyToken,
                input.AppSecret, userId }, cancellationToken: ct));
        return (await GetAsync(input.BranchId, ct))!;
    }
}
