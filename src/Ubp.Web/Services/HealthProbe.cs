using Microsoft.Extensions.Caching.Memory;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Web.Services;

/// <summary>
/// فحص جاهزية قاعدة البيانات لمسار الصحة العام.
/// يخزّن النتيجة مؤقتاً (15 ثانية) حتى لا يتحول كل probe إلى استعلام على القاعدة.
/// </summary>
public static class HealthProbe
{
    private const string CacheKey = "system.health";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(15);

    public static async Task<bool> IsHealthyAsync(ISchemaAdminRepository schema, IMemoryCache cache,
        CancellationToken ct)
    {
        if (cache.TryGetValue(CacheKey, out bool cached)) return cached;

        var healthy = (await schema.TestAsync(ct)).Ok;
        cache.Set(CacheKey, healthy, CacheDuration);
        return healthy;
    }
}
