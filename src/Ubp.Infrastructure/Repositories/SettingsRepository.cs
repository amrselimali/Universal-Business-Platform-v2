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
               time_zone AS TimeZone,
               session_timeout_minutes AS SessionTimeoutMinutes,
               password_min_length AS PasswordMinLength,
               max_failed_attempts AS MaxFailedAttempts,
               lockout_minutes AS LockoutMinutes,
               allow_negative_stock AS AllowNegativeStock,
               booking_default_due_days AS BookingDefaultDueDays,
               block_expired_issue AS BlockExpiredIssue,
               delivery_requires_collection AS DeliveryRequiresCollection,
               reserve_stock_on_booking AS ReserveStockOnBooking,
               auto_invoice_on_delivery AS AutoInvoiceOnDelivery,
               invoice_grouping_mode AS InvoiceGroupingMode,
               appointment_slot_minutes AS AppointmentSlotMinutes,
               pos_receipt_footer AS PosReceiptFooter,
               auto_follow_up_overdue AS AutoFollowUpOverdue,
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
                time_zone = @TimeZone,
                session_timeout_minutes = @SessionTimeoutMinutes,
                password_min_length = @PasswordMinLength,
                max_failed_attempts = @MaxFailedAttempts,
                lockout_minutes = @LockoutMinutes,
                allow_negative_stock = @AllowNegativeStock,
                booking_default_due_days = @BookingDefaultDueDays,
                block_expired_issue = @BlockExpiredIssue,
                delivery_requires_collection = @DeliveryRequiresCollection,
                reserve_stock_on_booking = @ReserveStockOnBooking,
                auto_invoice_on_delivery = @AutoInvoiceOnDelivery,
                invoice_grouping_mode = @InvoiceGroupingMode,
                appointment_slot_minutes = @AppointmentSlotMinutes,
                pos_receipt_footer = @PosReceiptFooter,
                auto_follow_up_overdue = @AutoFollowUpOverdue
            WHERE id = @Id AND version = @Version
            RETURNING id AS Id, company_name AS CompanyName, logo_url AS LogoUrl,
                      default_language AS DefaultLanguage, date_format AS DateFormat,
                      time_format AS TimeFormat, number_format AS NumberFormat,
                      time_zone AS TimeZone,
                      session_timeout_minutes AS SessionTimeoutMinutes,
                      password_min_length AS PasswordMinLength,
                      max_failed_attempts AS MaxFailedAttempts,
                      lockout_minutes AS LockoutMinutes,
                      allow_negative_stock AS AllowNegativeStock,
                      booking_default_due_days AS BookingDefaultDueDays,
                      block_expired_issue AS BlockExpiredIssue,
                      delivery_requires_collection AS DeliveryRequiresCollection,
                      reserve_stock_on_booking AS ReserveStockOnBooking,
                      auto_invoice_on_delivery AS AutoInvoiceOnDelivery,
                      invoice_grouping_mode AS InvoiceGroupingMode,
                      appointment_slot_minutes AS AppointmentSlotMinutes,
                      pos_receipt_footer AS PosReceiptFooter,
                      auto_follow_up_overdue AS AutoFollowUpOverdue,
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
