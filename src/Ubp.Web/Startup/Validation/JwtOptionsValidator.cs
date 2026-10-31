using Microsoft.Extensions.Options;
using Ubp.Infrastructure.Security;

namespace Ubp.Web.Startup.Validation;

/// <summary>يمنع الإقلاع بمفتاح JWT تطويري أو ضعيف في الإنتاج.</summary>
public sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    private static readonly string[] DevelopmentKeyMarkers =
        ["change-this", "change-me", "development", "dev-only", "local-development"];

    private readonly IHostEnvironment _environment;
    public JwtOptionsValidator(IHostEnvironment environment) => _environment = environment;

    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Key) || options.Key.Length < 32)
            return ValidateOptionsResult.Fail(
                "Jwt:Key is required and must be at least 32 characters long.");

        if (string.IsNullOrWhiteSpace(options.Issuer) || string.IsNullOrWhiteSpace(options.Audience))
            return ValidateOptionsResult.Fail("Jwt:Issuer and Jwt:Audience are required.");

        if (options.AccessTokenMinutes is < 1 or > 1440)
            return ValidateOptionsResult.Fail("Jwt:AccessTokenMinutes must be between 1 and 1440.");

        if (_environment.IsProduction()
            && DevelopmentKeyMarkers.Any(marker =>
                options.Key.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            return ValidateOptionsResult.Fail(
                "Jwt:Key looks like a development placeholder; supply a random key of 64+ characters.");

        return ValidateOptionsResult.Success;
    }
}
