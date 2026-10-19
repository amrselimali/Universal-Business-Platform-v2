using Ubp.Application.Contracts;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Web.Endpoints;

public static class SystemEndpoints
{
    public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/system/health", async (ISchemaAdminRepository schema,
            CancellationToken ct) =>
        {
            var test = await schema.TestAsync(ct);
            var tables = test.Ok ? await schema.GetTablesAsync(ct) : Array.Empty<TableInfo>();
            return Results.Ok(new
            {
                status = test.Ok ? "healthy" : "unhealthy",
                database = test.Ok ? "connected" : "disconnected",
                server = test.Server,
                databaseName = test.Database,
                coreTables = tables.Count
            });
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
