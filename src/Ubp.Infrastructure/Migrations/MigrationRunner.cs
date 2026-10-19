using System.Reflection;
using DbUp;
using Npgsql;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Migrations;

public sealed class MigrationReport
{
    public bool Success { get; init; }
    public List<string> ExecutedScripts { get; init; } = new();
    public string? Error { get; init; }
    public int TotalScripts { get; init; }
}

/// <summary>
/// نفس الـ runner يُستخدم عند بدء التطبيق، من زر شاشة قاعدة البيانات، وفي CI/CD.
/// يستخدم اتصال DDL منفصلاً ولا يلمس بيانات موجودة.
/// </summary>
public sealed partial class MigrationRunner
{
    private const long AdvisoryLockKey = 0x5542504C415446; // "UBPLATF"
    private readonly DatabaseOptions _options;

    public MigrationRunner(DatabaseOptions options) => _options = options;

    public MigrationReport Run()
    {
        if (string.IsNullOrWhiteSpace(_options.MigrationConnectionString))
            return new MigrationReport { Success = false, Error = "MIGRATION_CONNECTION_NOT_CONFIGURED" };

        using var lockConnection = new NpgsqlConnection(_options.MigrationConnectionString);
        lockConnection.Open();
        using (var lockCommand = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", lockConnection))
        {
            lockCommand.Parameters.AddWithValue("key", AdvisoryLockKey);
            var acquired = (bool)(lockCommand.ExecuteScalar() ?? false);
            if (!acquired)
                return new MigrationReport { Success = false, Error = "MIGRATION_ALREADY_RUNNING" };

            try
            {
                var upgrader = DeployChanges.To
                    .PostgresqlDatabase(_options.MigrationConnectionString)
                    .WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly(),
                        name => name.Contains(".Migrations.Scripts."))
                    .WithTransactionPerScript()
                    .Build();

                var result = upgrader.PerformUpgrade();
                return new MigrationReport
                {
                    Success = result.Successful,
                    ExecutedScripts = result.Scripts.Select(s => s.Name).ToList(),
                    TotalScripts = result.Scripts.Count(),
                    Error = result.Successful ? null : result.Error?.Message
                };
            }
            finally
            {
                using var unlock = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", lockConnection);
                unlock.Parameters.AddWithValue("key", AdvisoryLockKey);
                unlock.ExecuteNonQuery();
            }
        }
    }

    public IReadOnlyList<(string Name, DateTime AppliedAt)> GetAppliedScripts()
    {
        try
        {
            using var connection = new NpgsqlConnection(_options.MigrationConnectionString);
            connection.Open();
            using var command = new NpgsqlCommand(
                "SELECT scriptname, applied FROM schemaversions ORDER BY schemaversionsid", connection);
            using var reader = command.ExecuteReader();
            var list = new List<(string, DateTime)>();
            while (reader.Read())
            {
                var raw = reader.GetString(0);
                var index = raw.LastIndexOf(".Scripts.", StringComparison.Ordinal);
                var name = index >= 0 ? raw[(index + ".Scripts.".Length)..] : raw;
                list.Add((name, reader.GetDateTime(1)));
            }
            return list;
        }
        catch
        {
            return Array.Empty<(string, DateTime)>();
        }
    }
}
