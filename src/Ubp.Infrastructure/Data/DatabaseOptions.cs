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

    /// <summary>
    /// يحوّل رابط Neon/Postgres بصيغة URL إلى صيغة Npgsql القياسية.
    /// لا يُعطّل أي تحقق أمني: لا <c>TrustServerCertificate</c> ولا تجاهل للـ sslmode.
    /// </summary>
    private static string FormatConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return connectionString;

        if (!Uri.TryCreate(connectionString, UriKind.Absolute, out var uri)
            || (uri.Scheme != "postgres" && uri.Scheme != "postgresql"))
            return connectionString;

        var userInfo = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : ""
        };

        foreach (var pair in uri.Query.TrimStart('?').Split('&',
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0]);
            var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "";
            ApplyQueryParameter(builder, key, value);
        }

        return builder.ToString();
    }

    private static void ApplyQueryParameter(NpgsqlConnectionStringBuilder builder, string key,
        string value)
    {
        switch (key.ToLowerInvariant())
        {
            case "sslmode":
                // Neon يقدّم شهادات موثوقة؛ نطلب تحققاً كاملاً بدل الاكتفاء بالتشفير.
                builder.SslMode = value.ToLowerInvariant() switch
                {
                    "disable" => SslMode.Disable,
                    "allow" => SslMode.Allow,
                    "prefer" => SslMode.Prefer,
                    "require" or "verify-ca" or "verify-full" => SslMode.VerifyFull,
                    _ => builder.SslMode
                };
                break;
            case "channel_binding":
                builder.ChannelBinding = value.ToLowerInvariant() switch
                {
                    "disable" => ChannelBinding.Disable,
                    "prefer" => ChannelBinding.Prefer,
                    "require" => ChannelBinding.Require,
                    _ => builder.ChannelBinding
                };
                break;
            case "application_name":
                builder.ApplicationName = value;
                break;
            case "connect_timeout":
                if (int.TryParse(value, out var seconds) && seconds > 0)
                    builder.Timeout = seconds;
                break;
            case "options":
            case "sslcert":
            case "sslkey":
            case "sslrootcert":
                // معاملات لا تُترجم إلى Npgsql؛ تُدار عبر شهادة النظام أو متغيرات Npgsql.
                break;
            default:
                break;
        }
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