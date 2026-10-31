using Ubp.Infrastructure.Migrations;

namespace Ubp.Web.Startup;

/// <summary>
/// مسار النشر: تنفيذ الترحيلات من CI/CD ثم الخروج برمز حالة واضح (بلا تشغيل الويب).
/// </summary>
public static class MigrationCli
{
    public static bool IsRequested(string[] args) =>
        args.Any(a => string.Equals(a, "--migrate", StringComparison.OrdinalIgnoreCase));

    public static int Run(IServiceProvider services)
    {
        var report = services.GetRequiredService<MigrationRunner>().Run();
        Console.WriteLine(report.Success
            ? $"MIGRATIONS_OK applied={report.ExecutedScripts.Count} total={report.TotalScripts}"
            : $"MIGRATIONS_FAILED error={report.Error}");
        return report.Success ? 0 : 1;
    }
}
