using System.Reflection;

namespace Ubp.Infrastructure.Migrations;

public sealed partial class MigrationRunner
{
    public IReadOnlyList<string> GetEmbeddedScriptNames()
    {
        var prefix = "Ubp.Infrastructure.Migrations.Scripts.";
        return Assembly.GetExecutingAssembly()
            .GetManifestResourceNames()
            .Where(n => n.Contains(".Migrations.Scripts.", StringComparison.Ordinal))
            .Select(n =>
            {
                var index = n.IndexOf(prefix, StringComparison.Ordinal);
                return index >= 0 ? n[(index + prefix.Length)..] : n;
            })
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }
}
