using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace Ubp.Web.Services;

public sealed record ManualSourceFile(string RelativePath, string Text);

public sealed record ManualSourceSet(
    IReadOnlyList<ManualSourceFile> Pages,
    IReadOnlyList<ManualSourceFile> Components,
    IReadOnlyList<ManualSourceFile> Services)
{
    public static ManualSourceSet Empty { get; } = new([], [], []);
    public bool HasAny => Pages.Count + Components.Count + Services.Count > 0;
}

/// <summary>يقرأ ملفات الكود المصدري (Razor/الخدمات) من المصدر أو من مجلد Code المنشور.</summary>
public sealed class UserManualSourceReader
{
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<UserManualSourceReader> _logger;

    public UserManualSourceReader(IWebHostEnvironment environment, ILogger<UserManualSourceReader> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public ManualSourceSet Read() => new(
        ReadFiles(FindRoot("Pages"), "*.razor"),
        ReadFiles(FindRoot("Components"), "*.razor"),
        ReadFiles(FindServicesRoot(), "*.cs"));

    private string? FindRoot(string name)
    {
        foreach (var root in WebRoots())
        {
            var candidate = Path.Combine(root, name);
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }

    private string? FindServicesRoot()
    {
        var dev = Path.Combine(_environment.ContentRootPath, "..", "Ubp.Application", "Services");
        if (Directory.Exists(dev)) return dev;
        foreach (var root in WebRoots())
        {
            var candidate = Path.Combine(root, "Services");
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }

    private IEnumerable<string> WebRoots()
    {
        yield return _environment.ContentRootPath;
        yield return Path.Combine(_environment.ContentRootPath, "Code");
        yield return Path.Combine(AppContext.BaseDirectory, "Code");
    }

    private IReadOnlyList<ManualSourceFile> ReadFiles(string? root, string pattern)
    {
        if (root is null) return [];
        try
        {
            return Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => new ManualSourceFile(Path.GetRelativePath(root, path),
                    File.ReadAllText(path)))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read manual sources from {Root}", root);
            return [];
        }
    }
}
