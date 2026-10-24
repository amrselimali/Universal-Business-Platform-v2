using Dapper;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IAccountingSettingsRepository
{
    Task<AccountingSettings> GetAsync(CancellationToken ct = default);
    Task<AccountingSettings?> UpdateAsync(Guid id, int version, bool autoPostStockJournals,
        DateTime? accountingStartDate, Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<PostingDefault>> ListDefaultsAsync(CancellationToken ct = default);
    Task SetDefaultAsync(string settingKey, Guid? accountId, Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<ItemCategoryAccount>> ListCategoryAccountsAsync(CancellationToken ct = default);
    Task UpsertCategoryAccountAsync(Guid id, ItemCategoryAccountInput input, Guid userId,
        CancellationToken ct = default);
    Task<bool> DeleteCategoryAccountAsync(Guid categoryId, CancellationToken ct = default);
    Task<int> CountCategoriesWithoutAccountsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PostingDefaultType>> ListTypesAsync(CancellationToken ct = default);
}

public sealed partial class AccountingSettingsRepository : IAccountingSettingsRepository
{
    private readonly IDbConnectionFactory _factory;

    public AccountingSettingsRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<AccountingSettings> GetAsync(CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<AccountingSettings>(new CommandDefinition("""
            SELECT id AS Id, auto_post_stock_journals AS AutoPostStockJournals,
                   accounting_start_date AS AccountingStartDate, version AS Version
            FROM app_settings LIMIT 1
            """, cancellationToken: ct))
            ?? new AccountingSettings { Id = Guid.Empty };
    }

    public async Task<AccountingSettings?> UpdateAsync(Guid id, int version,
        bool autoPostStockJournals, DateTime? accountingStartDate, Guid userId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<AccountingSettings>(new CommandDefinition("""
            UPDATE app_settings
               SET auto_post_stock_journals = @autoPostStockJournals,
                   accounting_start_date = @accountingStartDate
             WHERE id = @id AND version = @version
            RETURNING id AS Id, auto_post_stock_journals AS AutoPostStockJournals,
                      accounting_start_date AS AccountingStartDate, version AS Version
            """, new { id, version, autoPostStockJournals, accountingStartDate, userId },
            cancellationToken: ct));
    }

    public async Task<IReadOnlyList<PostingDefault>> ListDefaultsAsync(CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<PostingDefault>(new CommandDefinition("""
            SELECT p.id AS Id, p.setting_key AS SettingKey, p.account_id AS AccountId,
                   a.code AS AccountCode, a.name_ar AS AccountName, p.is_active AS IsActive,
                   p.version AS Version
            FROM posting_defaults p LEFT JOIN accounts a ON a.id = p.account_id
            ORDER BY p.setting_key
            """, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task SetDefaultAsync(string settingKey, Guid? accountId, Guid userId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO posting_defaults (id, setting_key, account_id, created_by)
            VALUES (gen_random_uuid(), @settingKey, @accountId, @userId)
            ON CONFLICT (setting_key)
            DO UPDATE SET account_id = EXCLUDED.account_id, updated_by = @userId
            """, new { settingKey, accountId, userId }, cancellationToken: ct));
    }
}
