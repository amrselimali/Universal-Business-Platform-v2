using Microsoft.Extensions.Options;
using Ubp.Infrastructure.Data;
using Ubp.Infrastructure.Migrations;
using Ubp.Infrastructure.Security;
using Ubp.Web.Startup.Validation;

namespace Ubp.Web.Startup;

/// <summary>
/// يربط الإعدادات عبر نمط .NET القياسي (<see cref="IValidateOptions{TOptions}"/> + ValidateOnStart)
/// بدل الفحص اليدوي المبعثر داخل Program/ServiceRegistration.
/// </summary>
public static class OptionsRegistration
{
    public static IServiceCollection AddValidatedUbpOptions(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection("Database"))
            .ValidateOnStart();
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection("Jwt"))
            .ValidateOnStart();
        services.AddOptions<BootstrapOptions>()
            .Bind(configuration.GetSection("Bootstrap"))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<DatabaseOptions>, DatabaseOptionsValidator>();
        services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();
        services.AddSingleton<IValidateOptions<BootstrapOptions>, BootstrapOptionsValidator>();
        return services;
    }
}
