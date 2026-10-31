using Microsoft.Extensions.Caching.Memory;
using Ubp.Application.Contracts;
using Ubp.Infrastructure.Repositories;
using Ubp.Web.Services;

namespace Ubp.Web.Endpoints;

public static class SystemEndpoints
{
    public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        // فحص حياة بلا قاعدة بيانات (Liveness) — مناسب لموازن الحمل.
        app.MapGet("/health/live", () => Results.Ok(new { status = "alive" })).AllowAnonymous();

        // فحص جاهزية عام ومختصر: لا يكشف اسم السيرفر أو القاعدة أو عدد الجداول،
        // ويرجع 503 عند تعذر الاتصال حتى تلتقطه المراقبة.
        app.MapGet("/api/v1/system/health", async (ISchemaAdminRepository schema,
            IMemoryCache cache, CancellationToken ct) =>
        {
            var healthy = await HealthProbe.IsHealthyAsync(schema, cache, ct);
            return healthy
                ? Results.Ok(new { status = "healthy" })
                : Results.Json(new { status = "unhealthy" },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
        }).AllowAnonymous();

        app.MapGet("/api/v1/system/me", (HttpContext context) =>
        {
            var session = context.Items["session"] as SessionUser;
            if (session is null) return Results.Unauthorized();
            return Results.Ok(new
            {
                userId = session.User.Id,
                username = session.User.Username,
                name = session.User.Name,
                isAdmin = session.User.IsAdmin,
                isSuperAdmin = session.User.IsSuperAdmin,
                branchId = session.BranchId,
                branches = session.BranchIds,
                warehouses = session.WarehouseIds,
                language = session.UserLanguage
            });
        }).RequireAuthorization();
        return app;
    }
}
