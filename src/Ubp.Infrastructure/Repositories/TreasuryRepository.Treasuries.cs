using Dapper;
using Npgsql;
using Ubp.Core.Models;

namespace Ubp.Infrastructure.Repositories;

/// <summary>
/// تكويد الخزائن: إنشاء/تعديل/أرشفة، متابعة الشفت، وعمولة مزود الخدمة — 0093.
/// </summary>
public sealed partial class TreasuryRepository
{
    public async Task<Guid> CreateTreasuryAsync(Guid id, TreasuryInput input, Guid userId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO treasuries (id, branch_id, code, name_ar, name_en, treasury_type, account_id,
                opening_balance, over_short_account_id, collection_fee_percent,
                collection_fee_account_id, provider_fee_percent, provider_fee_amount,
                provider_fee_account_id, follow_in_shift, created_by)
            VALUES (@id, @BranchId, @Code, @NameAr, @NameEn, @TreasuryType, @AccountId,
                    @OpeningBalance, @OverShortAccountId, @CollectionFeePercent,
                    @CollectionFeeAccountId, @ProviderFeePercent, @ProviderFeeAmount,
                    @ProviderFeeAccountId, @FollowInShift, @userId)
            """, new { id, input.BranchId, input.Code, input.NameAr, input.NameEn,
                input.TreasuryType, input.AccountId, input.OpeningBalance,
                input.OverShortAccountId, input.CollectionFeePercent,
                input.CollectionFeeAccountId, input.ProviderFeePercent,
                input.ProviderFeeAmount, input.ProviderFeeAccountId, input.FollowInShift,
                userId }, tx, cancellationToken: ct));

        await SyncDefaultMethodAsync(connection, tx, id, userId, ct);
        await tx.CommitAsync(ct);
        return id;
    }

    /// <summary>تعديل خزينة مسجّلة (الفرع لا يتغير بعد الإنشاء) — 0093.</summary>
    public async Task<bool> UpdateTreasuryAsync(Guid id, TreasuryInput input, Guid userId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var updated = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE treasuries SET code = @Code, name_ar = @NameAr, name_en = @NameEn,
                treasury_type = @TreasuryType, account_id = @AccountId,
                opening_balance = @OpeningBalance,
                over_short_account_id = @OverShortAccountId,
                collection_fee_percent = @CollectionFeePercent,
                collection_fee_account_id = @CollectionFeeAccountId,
                provider_fee_percent = @ProviderFeePercent,
                provider_fee_amount = @ProviderFeeAmount,
                provider_fee_account_id = @ProviderFeeAccountId,
                follow_in_shift = @FollowInShift,
                updated_by = @userId, updated_at = now()
            WHERE id = @id AND is_active
            """, new { id, input.Code, input.NameAr, input.NameEn, input.TreasuryType,
                input.AccountId, input.OpeningBalance, input.OverShortAccountId,
                input.CollectionFeePercent, input.CollectionFeeAccountId,
                input.ProviderFeePercent, input.ProviderFeeAmount,
                input.ProviderFeeAccountId, input.FollowInShift, userId }, tx,
            cancellationToken: ct)) > 0;
        if (!updated)
        {
            await tx.RollbackAsync(ct);
            return false;
        }

        await SyncDefaultMethodAsync(connection, tx, id, userId, ct);
        await tx.CommitAsync(ct);
        return true;
    }

    /// <summary>أرشفة/استرجاع خزينة: إخفاؤها من الشاشات دون حذف حركاتها — 0093.</summary>
    public async Task<bool> SetTreasuryActiveAsync(Guid id, bool isActive, Guid userId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var updated = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE treasuries SET is_active = @isActive,
                is_main = CASE WHEN @isActive THEN is_main ELSE false END,
                updated_by = @userId, updated_at = now()
            WHERE id = @id
            """, new { id, isActive, userId }, tx, cancellationToken: ct)) > 0;
        if (!updated)
        {
            await tx.RollbackAsync(ct);
            return false;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE treasury_methods SET is_active = @isActive WHERE treasury_id = @id",
            new { id, isActive }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return true;
    }

    /// <summary>تشغيل/إيقاف متابعة الخزينة في الشفت — 0093.</summary>
    public async Task<bool> SetTreasuryFollowInShiftAsync(Guid id, bool followInShift,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var updated = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE treasuries SET follow_in_shift = @followInShift WHERE id = @id AND is_active",
            new { id, followInShift }, tx, cancellationToken: ct)) > 0;
        if (!updated)
        {
            await tx.RollbackAsync(ct);
            return false;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE treasury_methods SET follow_in_shift = @followInShift WHERE treasury_id = @id",
            new { id, followInShift }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return true;
    }


    /// <summary>
    /// عمولة التحصيل المحمَّلة من مزود الخدمة للخزينة (نسبة + مبلغ ثابت + حساب) — 0093.
    /// تُنسخ إلى طريقة الخزينة الافتراضية لتبقى حسابات التحصيل الحالية كما هي.
    /// </summary>
    public async Task<bool> SetProviderFeeAsync(Guid treasuryId, decimal percent, decimal amount,
        Guid? accountId, Guid userId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var updated = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE treasuries SET provider_fee_percent = @percent, provider_fee_amount = @amount,
                provider_fee_account_id = @accountId, updated_by = @userId, updated_at = now()
            WHERE id = @treasuryId AND is_active
            """, new { treasuryId, percent, amount, accountId, userId }, tx,
            cancellationToken: ct)) > 0;
        if (!updated)
        {
            await tx.RollbackAsync(ct);
            return false;
        }

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE treasury_methods SET fee_percent = @percent, fee_fixed = @amount,
                fee_account_id = @accountId
            WHERE treasury_id = @treasuryId AND is_active
            """, new { treasuryId, percent, amount, accountId }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return true;
    }

    /// <summary>
    /// مزامنة الطريقة الافتراضية للخزينة (تُستخدم داخلياً فقط في السندات ومستندات
    /// المصروفات) مع إعدادات الخزينة: الاسم والمتابعة وعمولة مزود الخدمة — 0093.
    /// </summary>
    private static async Task SyncDefaultMethodAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, Guid treasuryId, Guid userId, CancellationToken ct)
    {
        var treasury = await connection.QuerySingleOrDefaultAsync<(string NameAr, string? NameEn,
            string TreasuryType, bool FollowInShift, decimal ProviderFeePercent,
            decimal ProviderFeeAmount, Guid? ProviderFeeAccountId)>(new CommandDefinition("""
            SELECT name_ar AS NameAr, name_en AS NameEn, treasury_type AS TreasuryType,
                   follow_in_shift AS FollowInShift, provider_fee_percent AS ProviderFeePercent,
                   provider_fee_amount AS ProviderFeeAmount,
                   provider_fee_account_id AS ProviderFeeAccountId
            FROM treasuries WHERE id = @treasuryId
            """, new { treasuryId }, tx, cancellationToken: ct));
        if (string.IsNullOrWhiteSpace(treasury.NameAr)) return;

        var methodType = treasury.TreasuryType switch
        {
            TreasuryTypes.Cash => PaymentMethodTypes.Cash,
            TreasuryTypes.Wallet => PaymentMethodTypes.Wallet,
            _ => PaymentMethodTypes.Transfer
        };

        var updated = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE treasury_methods SET name_ar = @nameAr, name_en = @nameEn,
                follow_in_shift = @followInShift, fee_percent = @feePercent,
                fee_fixed = @feeAmount, fee_account_id = @feeAccountId
            WHERE treasury_id = @treasuryId AND code = 'DEFAULT'
            """, new
        {
            treasuryId, nameAr = treasury.NameAr, nameEn = treasury.NameEn,
            followInShift = treasury.FollowInShift, feePercent = treasury.ProviderFeePercent,
            feeAmount = treasury.ProviderFeeAmount, feeAccountId = treasury.ProviderFeeAccountId
        }, tx, cancellationToken: ct)) > 0;
        if (updated) return;

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO treasury_methods (treasury_id, code, name_ar, name_en, method_type,
                follow_in_shift, fee_percent, fee_fixed, fee_account_id, created_by)
            VALUES (@treasuryId, 'DEFAULT', @nameAr, @nameEn, @methodType, @followInShift,
                    @feePercent, @feeAmount, @feeAccountId, @userId)
            ON CONFLICT (treasury_id, code) DO NOTHING
            """, new
        {
            treasuryId, nameAr = treasury.NameAr, nameEn = treasury.NameEn, methodType,
            followInShift = treasury.FollowInShift, feePercent = treasury.ProviderFeePercent,
            feeAmount = treasury.ProviderFeeAmount, feeAccountId = treasury.ProviderFeeAccountId,
            userId
        }, tx, cancellationToken: ct));
    }

}
