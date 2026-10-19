using Microsoft.Extensions.Logging;
using Ubp.Core.Models;
using Ubp.Infrastructure.Repositories;
using Ubp.Infrastructure.Security;

namespace Ubp.Infrastructure.Migrations;

public sealed class BootstrapOptions
{
    public string SuperAdminUsername { get; set; } = "admin";
    public string SuperAdminPassword { get; set; } = "Admin@123";
    public string SuperAdminName { get; set; } = "مدير النظام";
}

/// <summary>يملأ البيانات الحرجة الناقصة دون المساس بأي بيانات موجودة.</summary>
public sealed class DataSeeder
{
    private readonly ISettingsRepository _settings;
    private readonly IUserRepository _users;
    private readonly PasswordHasher _hasher;
    private readonly BootstrapOptions _options;
    private readonly ILogger<DataSeeder> _logger;

    public DataSeeder(ISettingsRepository settings, IUserRepository users, PasswordHasher hasher,
        BootstrapOptions options, ILogger<DataSeeder> logger)
    {
        _settings = settings; _users = users; _hasher = hasher; _options = options; _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        await _settings.EnsureAsync("Universal Business Platform", "ar", ct);

        var username = string.IsNullOrWhiteSpace(_options.SuperAdminUsername)
            ? "admin" : _options.SuperAdminUsername.Trim();
        var existing = await _users.GetAuthByUsernameAsync(username, ct);
        if (existing is not null) return;

        var anySuperAdmin = await _users.CountActiveSuperAdminsAsync(ct) > 0;
        if (anySuperAdmin) return;

        var input = new UserInput
        {
            Username = username,
            Name = _options.SuperAdminName,
            IsActive = true,
            IsAdmin = true,
            IsSuperAdmin = true,
            Language = "ar"
        };
        await _users.CreateAsync(input, _hasher.Hash(_options.SuperAdminPassword), ct);
        _logger.LogWarning("Bootstrap Super Admin '{Username}' was created. Change the password immediately.",
            username);
    }
}
