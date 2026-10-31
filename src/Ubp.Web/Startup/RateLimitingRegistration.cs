using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Ubp.Web.Startup;

/// <summary>
/// تحصين شاشات الدخول (7E): حد ثابت لكل عنوان IP على محاولات الدخول/التحديث،
/// باستخدام Rate Limiter المدمج في ASP.NET Core (بلا أي حزمة إضافية).
/// </summary>
public static class RateLimitingRegistration
{
    public const string LoginPolicy = "login";

    public static IServiceCollection AddUbpRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(LoginPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));
        });
        return services;
    }
}
