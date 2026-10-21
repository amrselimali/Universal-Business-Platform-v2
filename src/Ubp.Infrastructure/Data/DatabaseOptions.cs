using Npgsql;

namespace Ubp.Infrastructure.Data;

public sealed class DatabaseOptions
{
    private string _connectionString = "";
    public string ConnectionString
    {
        get => _connectionString;
        set => _connectionString = FormatConnectionString(value);
    }

    private string _migrationConnectionString = "";
    public string MigrationConnectionString
    {
        get => _migrationConnectionString;
        set => _migrationConnectionString = FormatConnectionString(value);
    }

    public string QaConnectionString { get; set; } = "";
    public bool AutoMigrate { get; set; }
    public bool AutoMigrateQa { get; set; }

    private static string FormatConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return connectionString;

        // إذا كان الرابط مكتوب بصيغة URL الخاصة بـ Neon، نقوم بتحويله للصيغة القياسية لـ Npgsql تلقائياً
        if (connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var uri = new Uri(connectionString);
                var userInfo = uri.UserInfo.Split(':');
                var builder = new NpgsqlConnectionStringBuilder
                {
                    Host = uri.Host,
                    Port = uri.Port > 0 ? uri.Port : 5432,
                    Database = uri.AbsolutePath.TrimStart('/'),
                    Username = userInfo[0],
                    Password = userInfo.Length > 1 ? userInfo[1] : "",
                    SslMode = SslMode.Require,
                    TrustServerCertificate = true
                };
                return builder.ToString();
            }
            catch
            {
                return connectionString;
            }
        }

        return connectionString;
    }
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