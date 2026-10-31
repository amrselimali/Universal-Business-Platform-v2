using Xunit;

namespace Ubp.Tests;

/// <summary>
/// اختبار معماري: صفحات ومكوّنات الواجهة لا تتحدث مع طبقة البيانات مباشرة.
/// كل وصول للبيانات يمر عبر خدمات <c>Ubp.Application</c>، فيبقى اتجاه الاعتماد
/// Web → Application → Infrastructure ولا يُخترق.
/// </summary>
public sealed class ArchitectureTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void Pages_do_not_depend_on_the_infrastructure_layer()
    {
        var offenders = new List<string>();
        foreach (var dir in new[] { "Pages", "Components" })
        {
            var root = Path.Combine(RepoRoot, "src", "Ubp.Web", dir);
            if (!Directory.Exists(root)) continue;
            foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
            {
                if (!file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                    && !file.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)) continue;
                var text = File.ReadAllText(file);
                if (text.Contains("Ubp.Infrastructure", StringComparison.Ordinal))
                    offenders.Add(Path.GetRelativePath(RepoRoot, file));
            }
        }

        Assert.True(offenders.Count == 0,
            "UI files must not import Ubp.Infrastructure: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Startup_registrations_are_the_only_place_that_wires_repositories()
    {
        var webRoot = Path.Combine(RepoRoot, "src", "Ubp.Web");
        var allowed = new[]
        {
            Path.Combine(webRoot, "Startup"),
            Path.Combine(webRoot, "Services", "HealthProbe.cs"),
            Path.Combine(webRoot, "Security", "TokenIssuerAdapter.cs"),
            Path.Combine(webRoot, "Endpoints", "SystemEndpoints.cs")
        };

        var offenders = Directory.EnumerateFiles(webRoot, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".cs") || f.EndsWith(".razor"))
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}Pages{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}Components{Path.DirectorySeparatorChar}"))
            .Where(f => File.ReadAllText(f).Contains("Ubp.Infrastructure", StringComparison.Ordinal))
            .Where(f => !allowed.Any(a => f.StartsWith(a, StringComparison.Ordinal)))
            .Select(f => Path.GetRelativePath(RepoRoot, f))
            .ToList();

        Assert.True(offenders.Count == 0,
            "Ubp.Infrastructure is only allowed in Startup/known adapters: " +
            string.Join(", ", offenders));
    }

    [Fact]
    public void Source_files_stay_under_the_three_hundred_line_cap()
    {
        var offenders = new List<string>();
        foreach (var project in new[] { "src", "tests" })
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot, project), "*.*",
                         SearchOption.AllDirectories))
            {
                if (!file.EndsWith(".cs") && !file.EndsWith(".razor")) continue;
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                    continue;
                var lines = File.ReadAllLines(file).Length;
                if (lines > 300) offenders.Add($"{Path.GetRelativePath(RepoRoot, file)} ({lines})");
            }
        }

        Assert.True(offenders.Count == 0, "Files over 300 lines: " + string.Join(", ", offenders));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "UniversalBusinessPlatform.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
