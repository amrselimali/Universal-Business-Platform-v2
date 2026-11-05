using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface ITreasuryRepository : IShiftGate
{
    Task<Guid> CreateTreasuryAsync(Guid id, TreasuryInput input, Guid userId, CancellationToken ct = default);
    Task<Guid> CreateMethodAsync(Guid id, TreasuryMethodInput input, Guid userId, CancellationToken ct = default);
    /// <summary>تعديل طريقة دفع مسجّلة (نفس خزينتها) — 0075.</summary>
    Task<bool> UpdateMethodAsync(Guid id, TreasuryMethodInput input, CancellationToken ct = default);
    /// <summary>تحديد متابعة طريقة الدفع في الوردية — 0075.</summary>
    Task<bool> SetMethodFollowInShiftAsync(Guid id, bool followInShift, CancellationToken ct = default);
    Task<IReadOnlyList<Treasury>> ListTreasuriesAsync(Guid? branchId, CancellationToken ct = default);
    /// <summary>يجعل الخزينة رئيسية ويلغي غيرها (خزينة رئيسية واحدة فقط).</summary>
    Task<bool> SetMainAsync(Guid treasuryId, Guid userId, CancellationToken ct = default);
    Task<Guid?> GetMainAsync(CancellationToken ct = default);
    Task<IReadOnlyList<TreasuryMethod>> ListMethodsAsync(Guid treasuryId, CancellationToken ct = default);
    /// <summary>كل طرق التحصيل المكوَّدة في خزائن الفرع (لقراءتها من شاشة POS).</summary>
    Task<IReadOnlyList<TreasuryMethod>> ListBranchMethodsAsync(Guid branchId,
        CancellationToken ct = default);

    Task<Guid> CreateVoucherAsync(Guid id, VoucherInput input, Guid userId, CancellationToken ct = default);
    Task<Voucher?> GetVoucherAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Voucher>> ListVouchersAsync(Guid? branchId, string? voucherType, string? status,
        int limit, CancellationToken ct = default);
    Task<Guid> PostVoucherAsync(Guid id, Guid userId, CancellationToken ct = default);
    Task<bool> DeleteVoucherAsync(Guid id, CancellationToken ct = default);
    Task<bool> CancelVoucherAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<OpenDocument>> ListOpenDocumentsAsync(string partyType, Guid partyId,
        int limit, CancellationToken ct = default);
    Task<Guid> AllocateAsync(Guid voucherId, Guid documentId, decimal amount, Guid userId,
        CancellationToken ct = default);
    Task<IReadOnlyList<VoucherAllocation>> ListAllocationsAsync(Guid voucherId,
        CancellationToken ct = default);

    Task<Guid> OpenShiftAsync(Guid id, ShiftInput input, Guid userId, CancellationToken ct = default);
    /// <summary>إغلاق وردية الفرع بعدّ نقدية لكل خزينة (مسودة)؛ counts = null إغلاق آلي.</summary>
    Task<Shift> CloseShiftAsync(Guid shiftId, IReadOnlyList<ShiftCashCountInput>? counts,
        Guid userId, bool autoClosed, CancellationToken ct = default);
    /// <summary>عدّ النقدية المسجّل لكل خزينة في الوردية — 0076.</summary>
    Task<IReadOnlyList<ShiftCashCount>> ListCashCountsAsync(Guid shiftId,
        CancellationToken ct = default);
    /// <summary>تحديث القراءة الفعلية لعداد جهاز (تُرحَّل من الوردية) — 0078.</summary>
    Task<bool> SetMeterReadingAsync(Guid assetId, decimal value, Guid userId,
        CancellationToken ct = default);

    /// <summary>ترحيل الوردية للحسابات (تحويل حالتها إلى «تحت المراجعة») — 0079.</summary>
    Task<bool> PostShiftAsync(Guid shiftId, Guid userId, CancellationToken ct = default);

    /// <summary>حفظ ملاحظات الوردية (0078).</summary>
    Task<bool> SaveShiftNotesAsync(Guid shiftId, string? notes, CancellationToken ct = default);
    /// <summary>حساب زيادة/عجز خاص بالخزينة يُستخدم عند اعتماد المحاسب — 0076.</summary>
    Task<bool> SetTreasuryOverShortAccountAsync(Guid treasuryId, Guid? accountId,
        CancellationToken ct = default);
    Task<Shift?> GetOpenShiftAsync(Guid branchId, CancellationToken ct = default);
    Task<Shift?> GetShiftAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Shift>> ListShiftsAsync(Guid? branchId, DateTime? from, DateTime? to,
        int limit, CancellationToken ct = default);
    /// <summary>أرصدة افتتاح الوردية للفرع: مرحل من الحسابات + تحت المراجعة + عدادات الأجهزة.</summary>
    Task<ShiftOpeningBalances> GetShiftOpeningBalancesAsync(Guid branchId,
        CancellationToken ct = default);
    Task<ShiftReport> GetShiftReportAsync(Guid shiftId, CancellationToken ct = default);
    Task<IReadOnlyList<ShiftReviewItem>> ListReviewItemsAsync(Guid shiftId,
        CancellationToken ct = default);
    Task<Shift> ReviewShiftAsync(Guid shiftId, Guid userId, CancellationToken ct = default);
    /// <summary>الورديات المفتوحة التي تجاوزت موعد الإغلاق الآلي (5 صباحاً).</summary>
    Task<IReadOnlyList<Shift>> ListShiftsDueForAutoCloseAsync(DateTimeOffset cutoff,
        CancellationToken ct = default);
    Task<bool> HasOpenShiftAsync(Guid userId, Guid? branchId,
        IReadOnlyCollection<Guid> branchIds, CancellationToken ct = default);
    Task SaveDenominationsAsync(Guid shiftId, IReadOnlyList<ShiftDenominationInput> denominations,
        CancellationToken ct = default);
    Task<IReadOnlyList<ShiftDenomination>> ListDenominationsAsync(Guid shiftId,
        CancellationToken ct = default);
    Task<Shift> ReopenShiftAsync(Guid shiftId, string reason, Guid userId,
        CancellationToken ct = default);

    Task<IReadOnlyList<ClearingBalance>> ListClearingBalancesAsync(Guid? branchId,
        bool includeAllAccounts = false, CancellationToken ct = default);
    Task<Guid> SettleClearingAsync(Guid treasuryId, Guid clearingAccountId, decimal amount,
        DateTime docDate, string? reference, Guid userId, CancellationToken ct = default);
}

public sealed partial class TreasuryRepository : ITreasuryRepository
{
    private readonly IDbConnectionFactory _factory;
    public TreasuryRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<Guid> CreateTreasuryAsync(Guid id, TreasuryInput input, Guid userId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition("""
            INSERT INTO treasuries (id, branch_id, code, name_ar, name_en, treasury_type, account_id,
                opening_balance, over_short_account_id, created_by)
            VALUES (@id, @BranchId, @Code, @NameAr, @NameEn, @TreasuryType, @AccountId,
                    @OpeningBalance, @OverShortAccountId, @userId)
            RETURNING id
            """, new { id, input.BranchId, input.Code, input.NameAr, input.NameEn,
                input.TreasuryType, input.AccountId, input.OpeningBalance,
                input.OverShortAccountId, userId },
            cancellationToken: ct));
    }

    public async Task<Guid> CreateMethodAsync(Guid id, TreasuryMethodInput input, Guid userId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition("""
            INSERT INTO treasury_methods (id, treasury_id, code, name_ar, name_en, method_type,
                clearing_account_id, fee_account_id, fee_percent, fee_fixed, requires_reference,
                follow_in_shift, created_by)
            VALUES (@id, @TreasuryId, @Code, @NameAr, @NameEn, @MethodType, @ClearingAccountId,
                    @FeeAccountId, @FeePercent, @FeeFixed, @RequiresReference, @FollowInShift,
                    @userId)
            RETURNING id
            """, new { id, input.TreasuryId, input.Code, input.NameAr, input.NameEn,
                input.MethodType, input.ClearingAccountId, input.FeeAccountId, input.FeePercent,
                input.FeeFixed, input.RequiresReference, input.FollowInShift, userId },
            cancellationToken: ct));
    }

    /// <summary>تعديل طريقة دفع مسجّلة: كل الحقول ما عدا الخزينة (تبقى كما هي) — 0075.</summary>
    public async Task<bool> UpdateMethodAsync(Guid id, TreasuryMethodInput input,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE treasury_methods SET code = @Code, name_ar = @NameAr, name_en = @NameEn,
                method_type = @MethodType, clearing_account_id = @ClearingAccountId,
                fee_account_id = @FeeAccountId, fee_percent = @FeePercent,
                fee_fixed = @FeeFixed, requires_reference = @RequiresReference,
                follow_in_shift = @FollowInShift
            WHERE id = @id AND treasury_id = @TreasuryId AND is_active
            """, new { id, input.TreasuryId, input.Code, input.NameAr, input.NameEn,
                input.MethodType, input.ClearingAccountId, input.FeeAccountId, input.FeePercent,
                input.FeeFixed, input.RequiresReference, input.FollowInShift },
            cancellationToken: ct)) > 0;
    }

    /// <summary>تشغيل/إيقاف متابعة طريقة الدفع في الوردية — 0075.</summary>
    public async Task<bool> SetMethodFollowInShiftAsync(Guid id, bool followInShift,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE treasury_methods SET follow_in_shift = @followInShift " +
            "WHERE id = @id AND is_active", new { id, followInShift }, cancellationToken: ct)) > 0;
    }

    /// <summary>تحديد حساب زيادة/عجز الخزينة الذي يُرحّل عليه المحاسب — 0076.</summary>
    public async Task<bool> SetTreasuryOverShortAccountAsync(Guid treasuryId, Guid? accountId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE treasuries SET over_short_account_id = @accountId WHERE id = @treasuryId",
            new { treasuryId, accountId }, cancellationToken: ct)) > 0;
    }

    public async Task<IReadOnlyList<Treasury>> ListTreasuriesAsync(Guid? branchId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return (await connection.QueryAsync<Treasury>(new CommandDefinition("""
            SELECT t.id AS Id, t.branch_id AS BranchId, t.code AS Code, t.name_ar AS NameAr,
                   t.name_en AS NameEn, t.treasury_type AS TreasuryType, t.account_id AS AccountId,
                   t.is_main AS IsMain,
                   a.code AS AccountCode, a.name_ar AS AccountName, t.opening_balance AS OpeningBalance,
                   t.over_short_account_id AS OverShortAccountId, t.is_active AS IsActive
            FROM treasuries t JOIN accounts a ON a.id = t.account_id
            WHERE t.is_active AND (@branchId::uuid IS NULL OR t.branch_id = @branchId::uuid)
            ORDER BY t.code
            """, new { branchId }, cancellationToken: ct))).ToList();
    }

    public async Task<IReadOnlyList<TreasuryMethod>> ListMethodsAsync(Guid treasuryId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return (await connection.QueryAsync<TreasuryMethod>(new CommandDefinition("""
            SELECT m.id AS Id, m.treasury_id AS TreasuryId, m.code AS Code, m.name_ar AS NameAr,
                   m.method_type AS MethodType, m.clearing_account_id AS ClearingAccountId,
                   m.fee_account_id AS FeeAccountId,
                   m.fee_percent AS FeePercent, m.fee_fixed AS FeeFixed,
                   m.requires_reference AS RequiresReference, m.is_active AS IsActive,
                   m.follow_in_shift AS FollowInShift, t.name_ar AS TreasuryName
            FROM treasury_methods m
            JOIN treasuries t ON t.id = m.treasury_id
            WHERE m.treasury_id = @treasuryId AND m.is_active
            ORDER BY m.code
            """, new { treasuryId }, cancellationToken: ct))).ToList();
    }

    public async Task<IReadOnlyList<TreasuryMethod>> ListBranchMethodsAsync(Guid branchId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return (await connection.QueryAsync<TreasuryMethod>(new CommandDefinition("""
            SELECT m.id AS Id, m.treasury_id AS TreasuryId, m.code AS Code, m.name_ar AS NameAr,
                   m.method_type AS MethodType, m.clearing_account_id AS ClearingAccountId,
                   m.fee_account_id AS FeeAccountId,
                   m.fee_percent AS FeePercent, m.fee_fixed AS FeeFixed,
                   m.requires_reference AS RequiresReference, m.is_active AS IsActive,
                   m.follow_in_shift AS FollowInShift, t.name_ar AS TreasuryName
            FROM treasury_methods m
            JOIN treasuries t ON t.id = m.treasury_id
            WHERE t.branch_id = @branchId AND t.is_active AND m.is_active
            ORDER BY t.code, m.code
            """, new { branchId }, cancellationToken: ct))).ToList();
    }
}
