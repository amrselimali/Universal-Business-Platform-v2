using Ubp.Infrastructure.Data;
using Ubp.Infrastructure.Migrations;

namespace Ubp.Web.Startup;

public static class DatabaseBootstrapper
{
    public static async Task InitializeAsync(WebApplication app)
    {
        var options = app.Services.GetRequiredService<DatabaseOptions>();
        if (!options.AutoMigrate) return;

        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Bootstrap");
        var runner = app.Services.GetRequiredService<MigrationRunner>();
        var report = runner.Run();
        if (!report.Success)
        {
            logger.LogCritical("Database migrations failed: {Error}", report.Error);
            throw new InvalidOperationException(report.Error ?? "MIGRATION_FAILED");
        }
        logger.LogInformation("Database migrations applied: {Count} scripts", report.ExecutedScripts.Count);

        if (options.AutoMigrateQa && !string.IsNullOrWhiteSpace(options.QaConnectionString))
        {
            var qaOptions = new DatabaseOptions
            {
                ConnectionString = options.QaConnectionString,
                MigrationConnectionString = options.QaConnectionString,
                QaConnectionString = options.QaConnectionString
            };
            var qaReport = new MigrationRunner(qaOptions).Run();
            if (!qaReport.Success)
                logger.LogError("QA database migrations failed: {Error}", qaReport.Error);
            else
                logger.LogInformation("QA database migrations applied: {Count} scripts",
                    qaReport.ExecutedScripts.Count);
        }

        using var scope = app.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();
        await seeder.SeedAsync();
    }
}
