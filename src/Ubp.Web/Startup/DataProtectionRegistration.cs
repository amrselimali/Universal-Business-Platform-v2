using Microsoft.AspNetCore.DataProtection;

namespace Ubp.Web.Startup;

/// <summary>
/// يثبّت مفاتيح Data Protection على مسار دائم عند تكوينه، حتى تبقى الكوكيز/الرموز
/// صالحة بين عمليات إعادة التشغيل وعبر أكثر من نسخة (Scale-out).
/// </summary>
public static class DataProtectionRegistration
{
    public static IServiceCollection AddUbpDataProtection(this IServiceCollection services,
        IConfiguration configuration)
    {
        var builder = services.AddDataProtection().SetApplicationName("Ubp");
        var keyRingPath = configuration["DataProtection:KeyRingPath"];
        if (!string.IsNullOrWhiteSpace(keyRingPath))
            builder.PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
        return services;
    }
}
