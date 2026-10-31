using Npgsql;
using Ubp.Infrastructure.Data;

namespace Ubp.Web.Startup;

/// <summary>
/// تشخيص الإعدادات بلا كشف أسرار: يطبع البيئة، وهدف كل سلسلة اتصال (مضيف/منفذ/قاعدة/مستخدم)
/// مع كلمة المرور مقنّعة، ومسار ملف user-secrets ووجوده، ثم يحاول الاتصال فعلياً.
/// الاستخدام: dotnet run --project src/Ubp.Web -- --check-db
/// </summary>
public static class DiagnosticsCli
{
    public static bool IsRequested(string[] args) =>
        args.Any(a => string.Equals(a, "--check-db", StringComparison.OrdinalIgnoreCase));

    public static int Run(IServiceProvider services, string[] args)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var environment = services.GetRequiredService<IHostEnvironment>();
        var options = services.GetRequiredService<DatabaseOptions>();

        Console.WriteLine($"ENVIRONMENT={environment.EnvironmentName}");
        if (!environment.IsDevelopment())
            Console.WriteLine("WARNING: user-secrets تُقرأ في Development فقط — " +
                              "في غيرها مرّر القيم كمتغيرات بيئة (Database__ConnectionString).");

        Print("runtime    ", options.ConnectionString);
        Print("migrations ", options.MigrationConnectionString);
        Console.WriteLine($"AutoMigrate={options.AutoMigrate} AutoMigrateQa={options.AutoMigrateQa}");

        PrintSecretsFile();

        var ok = true;
        ok &= TryOpen("runtime", options.ConnectionString);
        ok &= TryOpen("migrations", options.MigrationConnectionString);
        Console.WriteLine(ok ? "DB_CHECK=OK" : "DB_CHECK=FAILED");
        return ok ? 0 : 1;
    }

    private static void Print(string label, string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.WriteLine($"{label}=EMPTY");
            return;
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            var password = builder.Password ?? "";
            Console.WriteLine($"{label}={builder.Host}:{builder.Port}/{builder.Database} " +
                              $"user={builder.Username} password={(password.Length == 0 ? "EMPTY" : $"***({password.Length})")} " +
                              $"ssl={builder.SslMode}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{label}=INVALID ({ex.Message})");
        }
    }

    private static void PrintSecretsFile()
    {
        try
        {
            var id = typeof(DiagnosticsCli).Assembly
                .GetCustomAttributes(typeof(Microsoft.Extensions.Configuration.UserSecrets.UserSecretsIdAttribute),
                    false)
                .OfType<Microsoft.Extensions.Configuration.UserSecrets.UserSecretsIdAttribute>()
                .FirstOrDefault()?.UserSecretsId;

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(appData))
                appData = Environment.GetEnvironmentVariable("APPDATA") ?? "";
            var root = Path.Combine(appData, "Microsoft", "UserSecrets");
            if (id is null)
            {
                Console.WriteLine($"USER_SECRETS=no UserSecretsId (expected dir {root})");
                return;
            }

            var file = Path.Combine(root, id, "secrets.json");
            Console.WriteLine($"USER_SECRETS={(File.Exists(file) ? "found" : "MISSING")} {file}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"USER_SECRETS=unknown ({ex.Message})");
        }
    }

    private static bool TryOpen(string label, string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return false;
        try
        {
            using var connection = new NpgsqlConnection(connectionString);
            connection.Open();
            using var command = new NpgsqlCommand("SELECT current_database()", connection);
            Console.WriteLine($"OPEN {label}=OK db={command.ExecuteScalar()}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"OPEN {label}=FAILED {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }
}
