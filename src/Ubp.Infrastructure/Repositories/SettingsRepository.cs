using Dapper;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface ISettingsRepository
{
    Task<AppSettings?> GetAsync(CancellationToken ct = default);
    Task<AppSettings?> UpdateAsync(AppSettings settings, CancellationToken ct = default);
    Task<bool> ExistsAsync(CancellationToken ct = default);
    Task EnsureAsync(string companyName, string language, CancellationToken ct = default);
}

public sealed class SettingsRepository : ISettingsRepository
{
    private readonly IDbConnectionFactory _factory;

    public SettingsRepository(IDbConnectionFactory factory) => _factory = factory;

    private const string SelectSql = """
        SELECT id AS Id, company_name AS CompanyName, logo_url AS LogoUrl,
               default_language AS DefaultLanguage, date_format AS DateFormat,
               time_format AS TimeFormat, number_format AS NumberFormat,
               session_timeout_minutes AS SessionTimeoutMinutes,
               password_min_length AS PasswordMinLength,
               max_failed_attempts AS MaxFailedAttempts,
               lockout_minutes AS LockoutMinutes,
               created_at AS CreatedAt, updated_at AS UpdatedAt, version AS Version
        FROM app_settings LIMIT 1
        """;

    public async Task<AppSettings?> GetAsync(CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<AppSettings>(
            new CommandDefinition(SelectSql, cancellationToken: ct));
    }

    public async Task<bool> ExistsAsync(CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition("SELECT EXISTS (SELECT 1 FROM app_settings)", cancellationToken: ct));
    }

    public async Task<AppSettings?> UpdateAsync(AppSettings settings, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE app_settings SET
                company_name = @CompanyName,
                logo_url = @LogoUrl,
                default_language = @DefaultLanguage,
                date_format = @DateFormat,
                time_format = @TimeFormat,
                number_format = @NumberFormat,
                session_timeout_minutes = @SessionTimeoutMinutes,
                password_min_length = @PasswordMinLength,
                max_failed_attempts = @MaxFailedAttempts,
                lockout_minutes = @LockoutMinutes
            WHERE id = @Id AND version = @Version
            RETURNING id AS Id, company_name AS CompanyName, logo_url AS LogoUrl,
                      default_language AS DefaultLanguage, date_format AS DateFormat,
                      time_format AS TimeFormat, number_format AS NumberFormat,
                      session_timeout_minutes AS SessionTimeoutMinutes,
                      password_min_length AS PasswordMinLength,
                      max_failed_attempts AS MaxFailedAttempts,
                      lockout_minutes AS LockoutMinutes,
                      created_at AS CreatedAt, updated_at AS UpdatedAt, version AS Version
            """;

        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<AppSettings>(
            new CommandDefinition(sql, settings, cancellationToken: ct));
    }

    public async Task EnsureAsync(string companyName, string language, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO app_settings (company_name, default_language)
            SELECT @companyName, @language
            WHERE NOT EXISTS (SELECT 1 FROM app_settings)
            """;
        await using var connection = await _factory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(sql,
            new { companyName, language = language == "en" ? "en" : "ar" }, cancellationToken: ct));
    }
}
