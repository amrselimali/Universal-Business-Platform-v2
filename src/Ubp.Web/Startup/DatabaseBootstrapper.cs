using Dapper;
using Npgsql;
using Ubp.Infrastructure.Data;
using Ubp.Infrastructure.Migrations;

namespace Ubp.Web.Startup;

public static class DatabaseBootstrapper
{
    public static async Task InitializeAsync(WebApplication app)
    {
        var options = app.Services.GetRequiredService<DatabaseOptions>();
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Bootstrap");

        if (options.AutoMigrate)
        {
            var runner = app.Services.GetRequiredService<MigrationRunner>();
            var report = runner.Run();
            if (!report.Success)
            {
                logger.LogCritical("Database migrations failed: {Error}", report.Error);
                throw new InvalidOperationException(report.Error ?? "MIGRATION_FAILED");
            }
            logger.LogInformation("Database migrations applied: {Count} scripts",
                report.ExecutedScripts.Count);

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
        }

        // البذر لا يعتمد على الترحيل التلقائي: في الإنتاج AutoMigrate=false، ومع ذلك
        // يجب أن يُنشأ/يُصحَّح حساب المدير من Bootstrap:SuperAdminPassword عند الإقلاع.
        if (!await SchemaReadyAsync(options, logger)) return;

        using var scope = app.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();
        await seeder.SeedAsync();
    }

    /// <summary>
    /// يتحقق من وجود الجداول الحرجة قبل البذر، حتى لا يفشل الإقلاع إذا كان
    /// الاتصال غير متاح مؤقتاً أو لم تُطبَّق الترحيلات بعد.
    /// </summary>
    private static async Task<bool> SchemaReadyAsync(DatabaseOptions options, ILogger logger)
    {
        try
        {
            await using var connection = new NpgsqlConnection(options.ConnectionString);
            await connection.OpenAsync();
            var ready = await connection.ExecuteScalarAsync<bool>(
                "SELECT to_regclass('public.users') IS NOT NULL " +
                "AND to_regclass('public.app_settings') IS NOT NULL");
            if (!ready)
                logger.LogWarning(
                    "Bootstrap seeding skipped: the database schema is not ready; run migrations first.");
            return ready;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Bootstrap seeding skipped: the database is not reachable.");
            return false;
        }
    }
}
