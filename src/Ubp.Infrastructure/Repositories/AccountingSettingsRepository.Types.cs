using Dapper;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class AccountingSettingsRepository
{
    public async Task<IReadOnlyList<PostingDefaultType>> ListTypesAsync(
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<PostingDefaultType>(new CommandDefinition("""
            SELECT key AS Key, module AS Module, party_type AS PartyType,
                   is_required AS IsRequired, sort_order AS SortOrder,
                   label_ar AS LabelAr, label_en AS LabelEn, is_active AS IsActive
            FROM posting_default_types ORDER BY sort_order, key
            """, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>قراءة الأنواع مع الحساب المرتبط — تُستخدم في شاشة الإعدادات.</summary>
    public async Task<IReadOnlyList<(PostingDefaultType Type, PostingDefault Default)>> ListTypedDefaultsAsync(
        CancellationToken ct = default)
    {
        var types = await ListTypesAsync(ct);
        var defaults = (await ListDefaultsAsync(ct))
            .ToDictionary(d => d.SettingKey, StringComparer.OrdinalIgnoreCase);

        return types
            .Select(t => (t, defaults.TryGetValue(t.Key, out var d)
                ? d
                : new PostingDefault { SettingKey = t.Key }))
            .ToList();
    }
}
