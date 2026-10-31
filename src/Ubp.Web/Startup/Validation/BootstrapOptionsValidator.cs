using Microsoft.Extensions.Options;
using Ubp.Infrastructure.Migrations;

namespace Ubp.Web.Startup.Validation;

/// <summary>يمنع إنشاء أو إقلاع النظام بكلمة مرور مدير افتراضية معروفة.</summary>
public sealed class BootstrapOptionsValidator : IValidateOptions<BootstrapOptions>
{
    private static readonly string[] WeakPasswords =
        ["admin", "Admin@123", "password", "P@ssw0rd", "123456", "ubp_app_dev_password"];

    private readonly IHostEnvironment _environment;
    public BootstrapOptionsValidator(IHostEnvironment environment) => _environment = environment;

    public ValidateOptionsResult Validate(string? name, BootstrapOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SuperAdminUsername))
            return ValidateOptionsResult.Fail("Bootstrap:SuperAdminUsername is required.");

        if (!_environment.IsProduction())
            return ValidateOptionsResult.Success;

        if (string.IsNullOrWhiteSpace(options.SuperAdminPassword))
            return ValidateOptionsResult.Fail(
                "Bootstrap:SuperAdminPassword is required in Production (no default value is shipped).");

        if (options.SuperAdminPassword.Length < 12)
            return ValidateOptionsResult.Fail(
                "Bootstrap:SuperAdminPassword must be at least 12 characters.");

        if (WeakPasswords.Contains(options.SuperAdminPassword, StringComparer.OrdinalIgnoreCase))
            return ValidateOptionsResult.Fail(
                "Bootstrap:SuperAdminPassword uses a well-known default; supply a unique strong password.");

        return ValidateOptionsResult.Success;
    }
}
