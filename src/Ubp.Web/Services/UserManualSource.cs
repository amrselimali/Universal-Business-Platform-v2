using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Ubp.Web.Services;

public sealed record UserManualContent(string Markdown, string? SourcePath, DateTimeOffset? UpdatedAt)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Markdown);
    public static UserManualContent Empty { get; } = new("", null, null);
}

/// <summary>
/// يقرأ دليل المستخدم من الملف على القرص أولاً (ليلتقط أي تحديث عند الضغط على زر التحديث)
/// ثم يسقط إلى النسخة المضمّنة في التطبيق إذا لم يوجد الملف.
/// </summary>
public sealed class UserManualSource
{
    private const string FileName = "USER-MANUAL-AR.md";
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<UserManualSource> _logger;

    public UserManualSource(IWebHostEnvironment environment, ILogger<UserManualSource> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public async Task<UserManualContent> LoadAsync(CancellationToken ct = default,
        IEnumerable<string>? searchPaths = null)
    {
        foreach (var path in (searchPaths ?? []).Concat(CandidatePaths()))
        {
            try
            {
                if (!File.Exists(path)) continue;
                var markdown = await File.ReadAllTextAsync(path, ct);
                return new UserManualContent(markdown, path, File.GetLastWriteTimeUtc(path));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read the user manual at {Path}", path);
            }
        }

        return await LoadEmbeddedAsync(ct);
    }

    private IEnumerable<string> CandidatePaths()
    {
        if (string.Equals(_environment.EnvironmentName, Environments.Development,
                StringComparison.OrdinalIgnoreCase))
            yield return Path.Combine(_environment.ContentRootPath, "..", "..", "docs", FileName);

        yield return Path.Combine(_environment.ContentRootPath, "Resources", FileName);
        yield return Path.Combine(AppContext.BaseDirectory, "Resources", FileName);
        yield return Path.Combine(Directory.GetCurrentDirectory(), "docs", FileName);
    }

    private async Task<UserManualContent> LoadEmbeddedAsync(CancellationToken ct)
    {
        var assembly = typeof(UserManualSource).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(FileName, StringComparison.OrdinalIgnoreCase));
        if (resourceName is null) return UserManualContent.Empty;

        await using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null) return UserManualContent.Empty;

        using var reader = new StreamReader(stream);
        return new UserManualContent(await reader.ReadToEndAsync(ct), $"embedded:{resourceName}", null);
    }
}
