using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Ubp.Web.Startup;

/// <summary>
/// تحصين شاشات الدخول (7E): حد ثابت لكل عنوان IP على محاولات الدخول فقط
/// (وليس تبديل الفرع/اللغة/الخروج أو بقية الواجهة) باستخدام Rate Limiter المدمج.
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
            {
                // localhost/الوكيل المحلي لا يُقيَّد (تشغيل تطويري/فحوص صحة/معاينة).
                var ip = context.Connection.RemoteIpAddress;
                if (ip is null || IPAddress.IsLoopback(ip))
                    return RateLimitPartition.GetNoLimiter("loopback");

                return RateLimitPartition.GetFixedWindowLimiter(ip.ToString(),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 20,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    });
            });
            options.OnRejected = async (ctx, token) =>
            {
                ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    ctx.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString();
                ctx.HttpContext.Response.ContentType = "application/json; charset=utf-8";
                await ctx.HttpContext.Response.WriteAsJsonAsync(new
                {
                    error = "auth.rate_limited",
                    message = "محاولات دخول كثيرة جدًا. انتظر دقيقة ثم أعد المحاولة."
                }, cancellationToken: token);
            };
        });
        return services;
    }
}

