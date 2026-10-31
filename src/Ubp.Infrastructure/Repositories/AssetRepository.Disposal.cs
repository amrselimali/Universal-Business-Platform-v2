using Dapper;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class AssetRepository
{
    /// <summary>حساب الخزينة النشطة الأولى للفرع (مصدر واحد: treasuries).</summary>
    public async Task<Guid?> BranchCashAccountAsync(Guid branchId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
            SELECT account_id FROM treasuries
            WHERE branch_id = @branchId AND is_active
            ORDER BY (treasury_type = 'cash') DESC, code
            LIMIT 1
            """, new { branchId }, cancellationToken: ct));
    }

    /// <summary>حساب افتراضي من posting_defaults (مصدر واحد لإعدادات الترحيل).</summary>
    public async Task<Guid?> DefaultAccountAsync(string key, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
            SELECT account_id FROM posting_defaults
            WHERE setting_key = @key AND is_active
            ORDER BY updated_at DESC NULLS LAST
            LIMIT 1
            """, new { key }, cancellationToken: ct));
    }

    /// <summary>مجمع الإهلاك الحالي للأصل (من سطور الإهلاك — مصدر واحد).</summary>
    public async Task<decimal> AccumulatedAsync(Guid assetId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<decimal>(new CommandDefinition("""
            SELECT COALESCE(SUM(amount), 0) FROM asset_depreciation_lines WHERE asset_id = @assetId
            """, new { assetId }, cancellationToken: ct));
    }

    public async Task<bool> MarkDisposedAsync(Guid assetId, decimal disposalAmount,
        Guid journalEntryId, Guid userId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE fixed_assets SET
                status = 'DISPOSED', disposed_at = now(), disposal_amount = @disposalAmount,
                journal_entry_id = @journalEntryId, updated_by = @userId, updated_at = now(),
                version = version + 1
            WHERE id = @assetId AND status <> 'DISPOSED'
            """, new { assetId, disposalAmount, journalEntryId, userId },
            cancellationToken: ct)) > 0;
    }
}
