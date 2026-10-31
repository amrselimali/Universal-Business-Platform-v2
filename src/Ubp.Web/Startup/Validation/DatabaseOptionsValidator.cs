using Microsoft.Extensions.Options;
using Npgsql;
using Ubp.Infrastructure.Data;

namespace Ubp.Web.Startup.Validation;

/// <summary>
/// يتحقق من إعدادات قاعدة البيانات في الإقلاع بدل الاتصال بقيم افتراضية أو بصلاحيات مالك.
/// </summary>
public sealed class DatabaseOptionsValidator : IValidateOptions<DatabaseOptions>
{
    private static readonly string[] OwnerRoles = ["postgres", "neondb_owner", "azure_superuser"];
    private static readonly string[] WeakPasswords =
        ["postgres", "password", "ubp_app_dev_password", "Admin@123", "change-me"];
    private static readonly string[] LocalHosts = ["localhost", "127.0.0.1", "::1", "postgres"];

    private readonly IHostEnvironment _environment;
    public DatabaseOptionsValidator(IHostEnvironment environment) => _environment = environment;

    public ValidateOptionsResult Validate(string? name, DatabaseOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            return ValidateOptionsResult.Fail(
                "Database:ConnectionString is required. Set it via environment variables or user-secrets.");

        if (options.AutoMigrate && string.IsNullOrWhiteSpace(options.MigrationConnectionString))
            return ValidateOptionsResult.Fail(
                "Database:AutoMigrate=true requires Database:MigrationConnectionString (a DDL role).");

        if (options.AutoMigrateQa && string.IsNullOrWhiteSpace(options.QaConnectionString))
            return ValidateOptionsResult.Fail(
                "Database:AutoMigrateQa=true requires Database:QaConnectionString.");

        if (!TryBuild(options.ConnectionString, out var app, out var error))
            return ValidateOptionsResult.Fail($"Database:ConnectionString is invalid: {error}");

        if (!string.IsNullOrWhiteSpace(options.MigrationConnectionString)
            && !TryBuild(options.MigrationConnectionString, out _, out var migrationError))
            return ValidateOptionsResult.Fail(
                $"Database:MigrationConnectionString is invalid: {migrationError}");

        if (!_environment.IsProduction() || IsLocal(app.Host))
            return ValidateOptionsResult.Success;

        if (OwnerRoles.Contains(app.Username, StringComparer.OrdinalIgnoreCase))
            return ValidateOptionsResult.Fail(
                $"Database:ConnectionString must use a dedicated application role without DDL rights (found '{app.Username}').");

        if (WeakPasswords.Contains(app.Password, StringComparer.OrdinalIgnoreCase))
            return ValidateOptionsResult.Fail(
                "Database:ConnectionString uses a well-known development password; rotate it before running in Production.");

        if (TryBuild(options.MigrationConnectionString, out var migration, out _)
            && migration.Host.Contains("-pooler.", StringComparison.OrdinalIgnoreCase))
            return ValidateOptionsResult.Fail(
                "Database:MigrationConnectionString must point to the direct endpoint, not a transaction pooler: " +
                "MigrationRunner relies on a session-level advisory lock.");

        return ValidateOptionsResult.Success;
    }

    private static bool TryBuild(string connectionString, out NpgsqlConnectionStringBuilder builder,
        out string? error)
    {
        try
        {
            builder = new NpgsqlConnectionStringBuilder(connectionString);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            builder = new NpgsqlConnectionStringBuilder();
            error = ex.Message;
            return false;
        }
    }

    private static bool IsLocal(string host) =>
        LocalHosts.Contains(host, StringComparer.OrdinalIgnoreCase);
}
