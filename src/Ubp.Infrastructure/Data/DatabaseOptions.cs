using Npgsql;

namespace Ubp.Infrastructure.Data;

public sealed class DatabaseOptions
{
    public string ConnectionString { get; set; } = "";
    public string MigrationConnectionString { get; set; } = "";
    public string QaConnectionString { get; set; } = "";
    public bool AutoMigrate { get; set; }
    public bool AutoMigrateQa { get; set; }
}

public interface IDbConnectionFactory
{
    Task<NpgsqlConnection> OpenAsync(CancellationToken ct = default);
    string ConnectionString { get; }
}

public sealed class NpgsqlConnectionFactory : IDbConnectionFactory
{
    private readonly DatabaseOptions _options;

    public NpgsqlConnectionFactory(DatabaseOptions options) => _options = options;

    public string ConnectionString => _options.ConnectionString;

    public async Task<NpgsqlConnection> OpenAsync(CancellationToken ct = default)
    {
        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct);
        return connection;
    }
}

public sealed class QaConnectionFactory : IDbConnectionFactory
{
    private readonly DatabaseOptions _options;

    public QaConnectionFactory(DatabaseOptions options) => _options = options;

    public string ConnectionString => _options.QaConnectionString;

    public async Task<NpgsqlConnection> OpenAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.QaConnectionString))
            throw new InvalidOperationException("QA_DATABASE_NOT_CONFIGURED");

        var connection = new NpgsqlConnection(_options.QaConnectionString);
        await connection.OpenAsync(ct);
        return connection;
    }
}
