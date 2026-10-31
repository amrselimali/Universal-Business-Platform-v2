using Microsoft.Extensions.Logging;
using Ubp.Core.Models;
using Ubp.Infrastructure.Repositories;
using Ubp.Infrastructure.Security;

namespace Ubp.Infrastructure.Migrations;

public sealed class BootstrapOptions
{
    public string SuperAdminUsername { get; set; } = "admin";

    /// <summary>
    /// لا توجد كلمة مرور افتراضية في الكود: تُوفَّر عبر Secrets/متغيرات البيئة فقط،
    /// وإلا فلا يُنشأ حساب المدير إطلاقاً.
    /// </summary>
    public string SuperAdminPassword { get; set; } = "";

    public string SuperAdminName { get; set; } = "مدير النظام";
}

/// <summary>يملأ البيانات الحرجة الناقصة دون المساس بأي بيانات موجودة.</summary>
public sealed class DataSeeder
{
    private const int MinPasswordLength = 12;
    private static readonly string[] KnownDefaultPasswords = ["Admin@123"];
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
        if (existing is not null)
        {
            await ReplaceKnownDefaultPasswordAsync(existing, ct);
            return;
        }

        var anySuperAdmin = await _users.CountActiveSuperAdminsAsync(ct) > 0;
        if (anySuperAdmin) return;

        if (string.IsNullOrWhiteSpace(_options.SuperAdminPassword)
            || _options.SuperAdminPassword.Length < MinPasswordLength)
        {
            _logger.LogError(
                "Bootstrap:SuperAdminPassword is missing or shorter than {Length} characters; " +
                "Super Admin '{Username}' was NOT created.", MinPasswordLength, username);
            return;
        }

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

    /// <summary>
    /// الترحيل `0009_seed.sql` المنفَّذ يزرع المدير بكلمة مرور افتراضية معروفة. لا نعدّل ترحيلاً
    /// منفَّذاً، بل نستبدل الكلمة الافتراضية بكلمة المرور المكوَّنة إن وُجدت، وإلا نسجّل خطأً حرجاً
    /// لتبقى الثغرة مرئية للمشغّل.
    /// </summary>
    private async Task ReplaceKnownDefaultPasswordAsync(User existing, CancellationToken ct)
    {
        if (!KnownDefaultPasswords.Any(p => _hasher.Verify(p, existing.PasswordHash)))
            return;

        var configured = _options.SuperAdminPassword;
        if (string.IsNullOrWhiteSpace(configured)
            || configured.Length < MinPasswordLength
            || KnownDefaultPasswords.Contains(configured, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogCritical(
                "Super Admin '{Username}' still uses the well-known default password. " +
                "Set Bootstrap:SuperAdminPassword (12+ characters) and restart immediately.",
                existing.Username);
            return;
        }

        await _users.ResetPasswordAsync(existing.Id, _hasher.Hash(configured), ct);
        _logger.LogWarning(
            "Super Admin '{Username}' still had the well-known default password; " +
            "it was replaced by the configured Bootstrap:SuperAdminPassword.", existing.Username);
    }
}
