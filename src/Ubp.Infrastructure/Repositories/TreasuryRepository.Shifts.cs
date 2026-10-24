using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class TreasuryRepository
{
    private sealed class ShiftRow
    {
        public Guid Id { get; set; }
        public Guid BranchId { get; set; }
        public Guid TreasuryId { get; set; }
        public string? TreasuryCode { get; set; }
        public string? TreasuryName { get; set; }
        public Guid UserId { get; set; }
        public string? Username { get; set; }
        public string Code { get; set; } = "";
        public DateTimeOffset OpenedAt { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }
        public decimal OpeningCash { get; set; }
        public decimal ExpectedCash { get; set; }
        public decimal? CountedCash { get; set; }
        public decimal? Variance { get; set; }
        public string Status { get; set; } = ShiftStatuses.Open;
        public string? Notes { get; set; }
    }

    private const string ShiftSelect = """
        SELECT s.id AS Id, s.branch_id AS BranchId, s.treasury_id AS TreasuryId,
               t.code AS TreasuryCode, t.name_ar AS TreasuryName, s.user_id AS UserId,
               u.username AS Username, s.code AS Code, s.opened_at AS OpenedAt,
               s.closed_at AS ClosedAt, s.opening_cash AS OpeningCash,
               s.expected_cash AS ExpectedCash, s.counted_cash AS CountedCash,
               s.variance AS Variance, s.status AS Status, s.notes AS Notes
        FROM cashier_shifts s
        JOIN treasuries t ON t.id = s.treasury_id
        LEFT JOIN users u ON u.id = s.user_id
        """;

    /// <summary>فتح وردية واحدة فقط لكل خزينة (الفهرس الفريد الجزئي يمنع التزاحم).</summary>
    public async Task<Guid> OpenShiftAsync(Guid id, ShiftInput input, Guid userId,
        CancellationToken ct = default)
    {
        if (input.OpeningCash < 0)
            throw new ValidationAppException("validation.shift_opening_cash");

        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var treasury = await GetTreasuryRowAsync(connection, tx, input.TreasuryId, ct)
            ?? throw new ValidationAppException("validation.treasury_not_found");
        if (treasury.BranchId != input.BranchId)
            throw new ValidationAppException("validation.treasury_branch_mismatch");

        var openId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT id FROM cashier_shifts WHERE treasury_id = @treasuryId AND status = 'open' " +
            "FOR UPDATE", new { treasuryId = input.TreasuryId }, tx, cancellationToken: ct));
        if (openId is not null)
            throw new ValidationAppException("validation.shift_already_open");

        var today = DateTime.Today;
        var code = await connection.ExecuteScalarAsync<string>(new CommandDefinition("""
            SELECT next_document_number('SHIFT'::varchar, @branchId::uuid, @docDate::date,
                                        'SHIFT'::varchar, 4::smallint)
            """, new { branchId = input.BranchId, docDate = today }, tx, cancellationToken: ct));

        try
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO cashier_shifts (id, branch_id, treasury_id, user_id, code, opened_at,
                    opening_cash, status, notes, created_by)
                VALUES (@id, @BranchId, @TreasuryId, @userId, @code, now(), @OpeningCash, 'open',
                        @Notes, @userId)
                """, new { id, input.BranchId, input.TreasuryId, userId, code, input.OpeningCash,
                    input.Notes }, tx, cancellationToken: ct));
        }
        catch (PostgresException ex) when (ex.ConstraintName == "ux_shift_open")
        {
            throw new ValidationAppException("validation.shift_already_open");
        }

        await tx.CommitAsync(ct);
        return id;
    }

    public async Task<Shift?> GetOpenShiftAsync(Guid treasuryId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var shift = await connection.QuerySingleOrDefaultAsync<ShiftRow>(new CommandDefinition(
            ShiftSelect + " WHERE s.treasury_id = @treasuryId AND s.status = 'open'",
            new { treasuryId }, cancellationToken: ct));
        if (shift is null) return null;
        return await MaterializeAsync(connection, shift, ct);
    }

    public async Task<Shift?> GetShiftAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var shift = await connection.QuerySingleOrDefaultAsync<ShiftRow>(new CommandDefinition(
            ShiftSelect + " WHERE s.id = @id", new { id }, cancellationToken: ct));
        if (shift is null) return null;
        return await MaterializeAsync(connection, shift, ct);
    }

    private static async Task<Shift> MaterializeAsync(NpgsqlConnection connection, ShiftRow row,
        CancellationToken ct)
    {
        var shift = new Shift
        {
            Id = row.Id, BranchId = row.BranchId, TreasuryId = row.TreasuryId,
            TreasuryCode = row.TreasuryCode, TreasuryName = row.TreasuryName, UserId = row.UserId,
            Username = row.Username, Code = row.Code, OpenedAt = row.OpenedAt,
            ClosedAt = row.ClosedAt, OpeningCash = row.OpeningCash,
            ExpectedCash = row.ExpectedCash, CountedCash = row.CountedCash, Variance = row.Variance,
            Status = row.Status, Notes = row.Notes
        };
        shift.Denominations = (await ListDenominationsAsync(connection, shift.Id, ct)).ToList();
        return shift;
    }
    /// <summary>
    /// إغلاق وردية: المتوقع = افتتاحي + قبض نقدي - صرف نقدي ± التحويلات النقديّة،
    /// والفرق يُرحّل على حساب زيادة/عجز الخزينة داخل نفس المعاملة.
    /// </summary>
    public async Task<Shift> CloseShiftAsync(Guid shiftId, decimal countedCash, Guid userId,
        CancellationToken ct = default)
    {
        countedCash = Math.Round(countedCash, 2, MidpointRounding.AwayFromZero);
        if (countedCash < 0)
            throw new ValidationAppException("validation.shift_counted_cash");

        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var shift = await connection.QuerySingleOrDefaultAsync<ShiftRow>(new CommandDefinition(
            ShiftSelect + " WHERE s.id = @shiftId FOR UPDATE OF s", new { shiftId }, tx,
            cancellationToken: ct)) ?? throw new NotFoundAppException();

        if (shift.Status != ShiftStatuses.Open)
            throw new ValidationAppException("validation.shift_not_open");

        var movement = await connection.ExecuteScalarAsync<decimal>(new CommandDefinition("""
            SELECT COALESCE(SUM(CASE
                       WHEN v.voucher_type = 'receipt' AND m.method_type = 'cash'
                           THEN l.amount - l.fee_amount
                       WHEN v.voucher_type = 'payment' AND m.method_type = 'cash'
                           THEN -(l.amount - l.fee_amount)
                       WHEN v.voucher_type = 'transfer' AND v.to_treasury_id = @treasuryId
                           THEN l.amount
                       WHEN v.voucher_type = 'transfer' AND v.treasury_id = @treasuryId
                           THEN -(l.amount + l.fee_amount)
                       ELSE 0 END), 0)
            FROM voucher_lines l
            JOIN vouchers v ON v.id = l.voucher_id
            JOIN treasury_methods m ON m.id = l.treasury_method_id
            WHERE v.status = 'posted' AND v.posted_at >= @openedAt
              AND (v.treasury_id = @treasuryId OR v.to_treasury_id = @treasuryId)
            """, new { treasuryId = shift.TreasuryId, openedAt = shift.OpenedAt }, tx,
            cancellationToken: ct));

        var expected = Math.Round(shift.OpeningCash + movement, 2, MidpointRounding.AwayFromZero);
        var variance = Math.Round(countedCash - expected, 2, MidpointRounding.AwayFromZero);

        if (variance != 0)
        {
            var treasury = await GetTreasuryRowAsync(connection, tx, shift.TreasuryId, ct)
                ?? throw new ValidationAppException("validation.treasury_not_found");
            var defaults = (await connection.QueryAsync<(string Key, Guid? AccountId)>(
                new CommandDefinition("SELECT setting_key, account_id FROM posting_defaults",
                    transaction: tx, cancellationToken: ct)))
                .Where(r => r.AccountId.HasValue)
                .ToDictionary(r => r.Key, r => r.AccountId!.Value, StringComparer.OrdinalIgnoreCase);

            if (!defaults.TryGetValue(PostingKeys.CashOverShort, out var overShort))
                throw new ValidationAppException("validation.posting_account_missing",
                    new Dictionary<string, string>
                    {
                        ["key"] = PostingKeys.CashOverShort, ["module"] = "treasury"
                    });

            var amount = Math.Abs(variance);
            var gl = variance > 0
                ? new List<JournalLine>
                {
                    new(treasury.AccountId, amount, 0),
                    new(overShort, 0, amount)
                }
                : new List<JournalLine>
                {
                    new(overShort, amount, 0),
                    new(treasury.AccountId, 0, amount)
                };

            await CreateVoucherEntryAsync(connection, tx, Guid.CreateVersion7(), treasury.TreasuryType,
                shift.BranchId, DateTime.Today, shift.Code,
                $"{(variance > 0 ? "زيادة" : "عجز")} وردية {shift.Code}", gl, userId, null, ct,
                "cash_shift");
        }

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE cashier_shifts SET expected_cash = @expected, counted_cash = @countedCash,
                variance = @variance, status = 'closed', closed_at = now(), closed_by = @userId,
                updated_by = @userId
            WHERE id = @shiftId
            """, new { expected, countedCash, variance, userId, shiftId }, tx,
            cancellationToken: ct));

        await tx.CommitAsync(ct);
        return (await GetShiftAsync(shiftId, ct))!;
    }

    public async Task<IReadOnlyList<Shift>> ListShiftsAsync(Guid? branchId, int limit,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<ShiftRow>(new CommandDefinition(
            ShiftSelect + """
             WHERE (@branchId::uuid IS NULL OR s.branch_id = @branchId::uuid)
             ORDER BY s.opened_at DESC, s.id DESC
             LIMIT @limit
            """, new { branchId, limit = Math.Clamp(limit, 1, 500) }, cancellationToken: ct));

        return rows.Select(row => new Shift
        {
            Id = row.Id, BranchId = row.BranchId, TreasuryId = row.TreasuryId,
            TreasuryCode = row.TreasuryCode, TreasuryName = row.TreasuryName, UserId = row.UserId,
            Username = row.Username, Code = row.Code, OpenedAt = row.OpenedAt,
            ClosedAt = row.ClosedAt, OpeningCash = row.OpeningCash,
            ExpectedCash = row.ExpectedCash, CountedCash = row.CountedCash, Variance = row.Variance,
            Status = row.Status, Notes = row.Notes
        }).ToList();
    }

    /// <summary>حفظ جدول الفئات النقدية للوردية المفتوحة (يستبدل السابق).</summary>
    public async Task SaveDenominationsAsync(Guid shiftId,
        IReadOnlyList<ShiftDenominationInput> denominations, CancellationToken ct = default)
    {
        foreach (var item in denominations)
        {
            if (item.Denomination <= 0)
                throw new ValidationAppException("validation.denomination_amount");
            if (item.Count < 0)
                throw new ValidationAppException("validation.denomination_count");
        }

        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var status = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT status FROM cashier_shifts WHERE id = @shiftId FOR UPDATE",
            new { shiftId }, tx, cancellationToken: ct)) ?? throw new NotFoundAppException();
        if (status != ShiftStatuses.Open)
            throw new ValidationAppException("validation.shift_not_open");

        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM shift_denominations WHERE shift_id = @shiftId", new { shiftId }, tx,
            cancellationToken: ct));

        foreach (var item in denominations)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO shift_denominations (id, shift_id, denomination, count)
                VALUES (@id, @shiftId, @denomination, @count)
                """, new
            {
                id = Guid.CreateVersion7(), shiftId,
                denomination = Math.Round(item.Denomination, 2, MidpointRounding.AwayFromZero),
                item.Count
            }, tx, cancellationToken: ct));
        }

        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<ShiftDenomination>> ListDenominationsAsync(Guid shiftId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return (await ListDenominationsAsync(connection, shiftId, ct)).ToList();
    }

    private static async Task<IReadOnlyList<ShiftDenomination>> ListDenominationsAsync(
        NpgsqlConnection connection, Guid shiftId, CancellationToken ct) =>
        (await connection.QueryAsync<ShiftDenomination>(new CommandDefinition("""
            SELECT id AS Id, denomination AS Denomination, count AS Count
            FROM shift_denominations WHERE shift_id = @shiftId
            ORDER BY denomination DESC
            """, new { shiftId }, cancellationToken: ct))).ToList();

}
