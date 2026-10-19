using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Migrations;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

public sealed record DatabaseStatus(bool Connected, string? Server, string? Database, string? User,
    string? Error, IReadOnlyList<TableInfo> Tables, IReadOnlyList<string> Applied,
    IReadOnlyList<string> Pending);

public sealed record MigrationOutcome(bool Success, string? Error, IReadOnlyList<string> Executed);

public sealed partial class DatabaseAdminService
{
    private readonly ISchemaAdminRepository _schema;
    private readonly MigrationRunner _runner;
    private readonly DataSeeder _seeder;
    private readonly PermissionService _permissions;
    private readonly ISettingsService _settings;
    private readonly AuditService _audit;

    public DatabaseAdminService(ISchemaAdminRepository schema, MigrationRunner runner,
        DataSeeder seeder, PermissionService permissions, ISettingsService settings, AuditService audit)
    {
        _schema = schema; _runner = runner; _seeder = seeder;
        _permissions = permissions; _settings = settings; _audit = audit;
    }

    private static void RequireSuperAdmin(ActorContext actor)
    {
        if (!actor.IsSuperAdmin) throw new ForbiddenException();
    }

    public async Task<DatabaseStatus> GetStatusAsync(ActorContext actor, CancellationToken ct = default)
    {
        RequireSuperAdmin(actor);
        await _permissions.RequireAsync(actor, ScreenCodes.DatabaseAdmin, ActionCodes.View, ct: ct);
        var test = await _schema.TestAsync(ct);
        var tables = test.Ok ? await _schema.GetTablesAsync(ct) : Array.Empty<TableInfo>();
        var applied = _runner.GetAppliedScripts().Select(a => a.Name).ToList();
        var appliedSet = applied.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pending = _runner.GetEmbeddedScriptNames().Where(s => !appliedSet.Contains(s)).ToList();
        return new DatabaseStatus(test.Ok, test.Server, test.Database, test.User, test.Error,
            tables, applied, pending);
    }

    public async Task<MigrationOutcome> RunMigrationsAsync(ActorContext actor,
        CancellationToken ct = default)
    {
        RequireSuperAdmin(actor);
        await _permissions.RequireAsync(actor, ScreenCodes.DatabaseAdmin, ActionCodes.RunMigrations, ct: ct);
        var report = _runner.Run();
        if (report.Success)
        {
            await _seeder.SeedAsync(ct);
            _settings.Invalidate();
        }
        await _audit.WriteAsync(actor, "schemaversions", "migrations",
            "MIGRATION", null, new { report.Success, report.ExecutedScripts, report.Error }, ct);
        return new MigrationOutcome(report.Success, report.Error, report.ExecutedScripts);
    }

    public async Task<RowPage> GetRowsAsync(ActorContext actor, string table, int page,
        CancellationToken ct = default)
    {
        RequireSuperAdmin(actor);
        await _permissions.RequireAsync(actor, ScreenCodes.DatabaseAdmin, ActionCodes.View, ct: ct);
        return await _schema.GetRowsAsync(table, page, ct);
    }

    public async Task<IReadOnlyList<ColumnInfo>> GetColumnsAsync(ActorContext actor, string table,
        CancellationToken ct = default)
    {
        RequireSuperAdmin(actor);
        await _permissions.RequireAsync(actor, ScreenCodes.DatabaseAdmin, ActionCodes.View, ct: ct);
        return await _schema.GetColumnsAsync(table, ct);
    }
}
