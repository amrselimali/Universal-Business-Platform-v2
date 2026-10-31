using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Ubp.Infrastructure.Data;
using Ubp.Infrastructure.Migrations;
using Ubp.Infrastructure.Security;
using Ubp.Web.Startup.Validation;
using Xunit;

namespace Ubp.Tests;

/// <summary>
/// اختبارات قواعد الإقلاع الآمن (لا تحتاج قاعدة بيانات):
/// تمنع القيم الافتراضية المعروفة وتُفشل الإقلاع بدل الاتصال بإعدادات خطرة.
/// </summary>
public sealed class StartupOptionsTests
{
    private sealed class FakeEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Ubp.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private const string StrongKey = "0123456789abcdef0123456789abcdef0123456789abcdef";

    private static string Remote(string user, string password) =>
        $"Host=ep-test.eu-central-1.aws.neon.tech;Port=5432;Database=neondb;" +
        $"Username={user};Password={password};SSL Mode=VerifyFull";

    private static string Local(string user, string password) =>
        $"Host=localhost;Port=5432;Database=ubp;Username={user};Password={password}";

    [Fact]
    public void Jwt_key_shorter_than_32_characters_is_rejected()
    {
        var validator = new JwtOptionsValidator(new FakeEnvironment("Development"));
        var result = validator.Validate(null, new JwtOptions { Key = "short-key" });
        Assert.True(result.Failed);
    }

    [Fact]
    public void Development_jwt_key_is_rejected_in_production()
    {
        var validator = new JwtOptionsValidator(new FakeEnvironment("Production"));
        var result = validator.Validate(null, new JwtOptions
        {
            Key = "CHANGE-THIS-DEVELOPMENT-KEY-32-CHARS-MINIMUM-0001"
        });
        Assert.True(result.Failed);
        Assert.Contains("development placeholder", result.FailureMessage);
    }

    [Fact]
    public void Strong_random_key_passes_in_production()
    {
        var validator = new JwtOptionsValidator(new FakeEnvironment("Production"));
        Assert.True(validator.Validate(null, new JwtOptions { Key = StrongKey }).Succeeded);
    }

    [Fact]
    public void Bootstrap_password_is_required_and_not_the_known_default_in_production()
    {
        var validator = new BootstrapOptionsValidator(new FakeEnvironment("Production"));

        Assert.True(validator.Validate(null, new BootstrapOptions()).Failed);
        Assert.True(validator.Validate(null, new BootstrapOptions
        {
            SuperAdminPassword = "Admin@123"
        }).Failed);
        Assert.True(validator.Validate(null, new BootstrapOptions
        {
            SuperAdminPassword = "Short!123"
        }).Failed);
        Assert.True(validator.Validate(null, new BootstrapOptions
        {
            SuperAdminPassword = "Str0ng-Unique-Pass-2026"
        }).Succeeded);
    }

    [Fact]
    public void Missing_connection_string_fails_even_in_development()
    {
        var validator = new DatabaseOptionsValidator(new FakeEnvironment("Development"));
        Assert.True(validator.Validate(null, new DatabaseOptions()).Failed);
    }

    [Fact]
    public void Production_rejects_the_database_owner_role_for_the_runtime_connection()
    {
        var validator = new DatabaseOptionsValidator(new FakeEnvironment("Production"));
        var options = new DatabaseOptions
        {
            ConnectionString = Remote("neondb_owner", "Str0ng-Unique-Pass-2026"),
            MigrationConnectionString = Remote("neondb_owner", "Str0ng-Unique-Pass-2026")
        };

        var result = validator.Validate(null, options);
        Assert.True(result.Failed);
        Assert.Contains("dedicated application role", result.FailureMessage);
    }

    [Fact]
    public void Production_rejects_a_transaction_pooler_for_migrations()
    {
        var validator = new DatabaseOptionsValidator(new FakeEnvironment("Production"));
        var options = new DatabaseOptions
        {
            ConnectionString = Remote("ubp_app", "Str0ng-Unique-Pass-2026"),
            MigrationConnectionString =
                "Host=ep-test-pooler.eu-central-1.aws.neon.tech;Database=neondb;" +
                "Username=neondb_owner;Password=Str0ng-Unique-Pass-2026"
        };

        var result = validator.Validate(null, options);
        Assert.True(result.Failed);
        Assert.Contains("direct endpoint", result.FailureMessage);
    }

    [Fact]
    public void Local_development_database_stays_usable_with_compose_values()
    {
        var validator = new DatabaseOptionsValidator(new FakeEnvironment("Production"));
        var options = new DatabaseOptions
        {
            ConnectionString = Local("ubp_app", "ubp_app_dev_password"),
            MigrationConnectionString = Local("postgres", "postgres")
        };

        Assert.True(validator.Validate(null, options).Succeeded);
    }

    [Fact]
    public void Neon_url_is_converted_without_disabling_certificate_validation()
    {
        var options = new DatabaseOptions
        {
            ConnectionString =
                "postgresql://ubp_app:p%40ss%3Aword@ep-test-pooler.eu-central-1.aws.neon.tech/neondb" +
                "?sslmode=require&channel_binding=require"
        };

        Assert.Contains("Host=ep-test-pooler", options.ConnectionString);
        Assert.Contains("Password=p@ss:word", options.ConnectionString);
        Assert.Contains("SSL Mode=VerifyFull", options.ConnectionString);
        Assert.Contains("Channel Binding=Require", options.ConnectionString);
        Assert.DoesNotContain("Trust Server Certificate=true", options.ConnectionString);
    }
}
