using Microsoft.Extensions.Caching.Memory;
using Ubp.Core.Results;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

/// <summary>
/// جاهزية الترحيل لكل موديول (N3): تتحقق من ربط مفاتيح posting_default_types الإلزامية
/// بحسابات نشطة غير مجمّعة، وتُخزّن النتيجة في الذاكرة وتُمسح عند أي تعديل على الإعدادات.
/// </summary>
public sealed class PostingReadinessService
{
    private static int _version;
    public static void InvalidateAll() => Interlocked.Increment(ref _version);

    private readonly IAccountingSettingsRepository _settings;
    private readonly IAccountRepository _accounts;
    private readonly IMemoryCache _cache;

    public PostingReadinessService(IAccountingSettingsRepository settings, IAccountRepository accounts,
        IMemoryCache cache)
    {
        _settings = settings; _accounts = accounts; _cache = cache;
    }

    /// <summary>يرجع مفاتيح الموديول الإلزامية غير الجاهزة (فارغة = جاهز).</summary>
    public async Task<IReadOnlyList<string>> MissingAsync(string module, CancellationToken ct = default)
    {
        var cacheKey = $"posting-ready:{module}:{Volatile.Read(ref _version)}";
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<string>? cached) && cached is not null)
            return cached;

        var types = (await _settings.ListTypesAsync(ct))
            .Where(t => t.IsActive && t.IsRequired &&
                        string.Equals(t.Module, module, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.SortOrder)
            .ToList();
        var defaults = (await _settings.ListDefaultsAsync(ct))
            .Where(d => d.AccountId.HasValue)
            .ToDictionary(d => d.SettingKey, d => d.AccountId!.Value, StringComparer.OrdinalIgnoreCase);

        var missing = new List<string>();
        foreach (var type in types)
        {
            if (!defaults.TryGetValue(type.Key, out var accountId))
            {
                missing.Add(type.Key);
                continue;
            }

            var account = await _accounts.GetAsync(accountId, ct);
            if (account is null || !account.IsActive || account.IsGroup)
                missing.Add(type.Key);
        }

        _cache.Set(cacheKey, (IReadOnlyList<string>)missing, TimeSpan.FromMinutes(5));
        return missing;
    }

    /// <summary>يُستخدم داخل خدمة الترحيل قبل إنشاء القيد: يرفض الترحيل ويسمّي المفتاح الناقص.</summary>
    public async Task RequireAsync(string module, CancellationToken ct = default)
    {
        var missing = await MissingAsync(module, ct);
        if (missing.Count == 0) return;

        throw new ValidationAppException("validation.posting_account_missing",
            new Dictionary<string, string>
            {
                ["key"] = missing[0],
                ["count"] = missing.Count.ToString(),
                ["module"] = module
            });
    }
}
