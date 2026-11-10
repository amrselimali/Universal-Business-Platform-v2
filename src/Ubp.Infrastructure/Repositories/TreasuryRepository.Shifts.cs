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
        public string? BranchName { get; set; }
        public Guid? TreasuryId { get; set; }
        public string? TreasuryCode { get; set; }
        public string? TreasuryName { get; set; }
        public Guid UserId { get; set; }
        public string? Username { get; set; }
        public string Code { get; set; } = "";
        public DateTimeOffset OpenedAt { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }
        public Guid? ClosedBy { get; set; }
        public string? ClosedByName { get; set; }
        public decimal OpeningCash { get; set; }
        public decimal CarriedBalance { get; set; }
        public decimal ReviewBalance { get; set; }
        public decimal MeterBalance { get; set; }
        public decimal ExpectedCash { get; set; }
        public decimal? CountedCash { get; set; }
        public decimal? Variance { get; set; }
        public decimal ReceiptsTotal { get; set; }
        public decimal TreasuryBalance { get; set; }
        public string Status { get; set; } = ShiftStatuses.Open;
        public string? Notes { get; set; }
        public DateTimeOffset? ReopenedAt { get; set; }
        public string? ReopenReason { get; set; }
        public string ReviewStatus { get; set; } = ShiftReviewStatuses.None;
        public bool AutoClosed { get; set; }
        public DateTimeOffset? ReviewedAt { get; set; }
    }

    private const string ShiftSelect = """
        SELECT s.id AS Id, s.branch_id AS BranchId, br.name_ar AS BranchName,
               s.treasury_id AS TreasuryId, t.code AS TreasuryCode, t.name_ar AS TreasuryName,
               s.user_id AS UserId, u.username AS Username, s.code AS Code,
               s.opened_at AS OpenedAt, s.closed_at AS ClosedAt, s.opening_cash AS OpeningCash,
               s.closed_by AS ClosedBy,
               (SELECT u2.username FROM users u2 WHERE u2.id = s.closed_by) AS ClosedByName,
               s.carried_balance AS CarriedBalance, s.review_balance AS ReviewBalance,
               s.meter_balance AS MeterBalance, s.expected_cash AS ExpectedCash,
               s.counted_cash AS CountedCash, s.variance AS Variance, s.status AS Status,
               s.notes AS Notes, s.reopened_at AS ReopenedAt, s.reopen_reason AS ReopenReason,
               s.review_status AS ReviewStatus, s.auto_closed AS AutoClosed,
               s.reviewed_at AS ReviewedAt,
               COALESCE((SELECT SUM(v.total_amount) FROM vouchers v
                         WHERE v.branch_id = s.branch_id AND v.voucher_type = 'receipt'
                           AND v.status <> 'cancelled'
                           AND v.created_at >= s.opened_at
                           AND (s.closed_at IS NULL OR v.created_at <= s.closed_at)), 0)
                   AS ReceiptsTotal,
               COALESCE((SELECT SUM(COALESCE(c.counted_cash, c.expected_cash))
                         FROM shift_cash_counts c WHERE c.shift_id = s.id), 0) AS TreasuryBalance
        FROM cashier_shifts s
        JOIN branches br ON br.id = s.branch_id
        LEFT JOIN treasuries t ON t.id = s.treasury_id
        LEFT JOIN users u ON u.id = s.user_id
        """;

    /// <summary>
    /// فتح شفت للفرع كله (كل الشاشات): شفت واحدة مفتوحة لكل فرع، ورصيد الافتتاح
    /// يُحسب آلياً من الحسابات + الشفتات تحت المراجعة ولا يُدخل يدوياً.
    /// </summary>
    public async Task<Guid> OpenShiftAsync(Guid id, ShiftInput input, Guid userId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var branchExists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM branches WHERE id = @branchId AND is_active)",
            new { branchId = input.BranchId }, tx, cancellationToken: ct));
        if (!branchExists)
            throw new ValidationAppException("validation.branch_not_found");

        var openId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT id FROM cashier_shifts WHERE branch_id = @branchId AND status = 'open' " +
            "FOR UPDATE", new { branchId = input.BranchId }, tx, cancellationToken: ct));
        if (openId is not null)
            throw new ValidationAppException("validation.shift_already_open");

        var balances = await GetShiftOpeningBalancesAsync(connection, tx, input.BranchId, ct);
        var code = await connection.ExecuteScalarAsync<string>(new CommandDefinition("""
            SELECT next_document_number('SHIFT'::varchar, @branchId::uuid, @docDate::date,
                                        'SHIFT'::varchar, 4::smallint)
            """, new { branchId = input.BranchId, docDate = DateTime.Today }, tx,
            cancellationToken: ct));

        try
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO cashier_shifts (id, branch_id, user_id, code, opened_at, opening_cash,
                    carried_balance, review_balance, meter_balance, status, notes, created_by)
                VALUES (@id, @BranchId, @userId, @code, now(), @openingCash, @carried, @review,
                        @meter, 'open', @Notes, @userId)
                """, new
            {
                id, input.BranchId, userId, code, openingCash = balances.OpeningCash,
                carried = balances.CarriedFromAccounts, review = balances.PendingReview,
                meter = balances.MeterBalance, input.Notes
            }, tx, cancellationToken: ct));
        }
        catch (PostgresException ex) when (ex.ConstraintName == "ux_shift_open")
        {
            throw new ValidationAppException("validation.shift_already_open");
        }

        // لقطة افتتاح كل خزينة متابَعة: تُستخدم لحساب المتوقع عند الإغلاق وترحيل الفرق.
        foreach (var row in balances.Treasuries)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO shift_cash_counts (id, shift_id, treasury_id, opening_cash,
                    expected_cash, status)
                VALUES (@id, @shiftId, @treasuryId, @opening, @opening, 'open')
                ON CONFLICT (shift_id, treasury_id) DO NOTHING
                """, new
            {
                id = Guid.CreateVersion7(), shiftId = id, treasuryId = row.TreasuryId,
                opening = row.Total
            }, tx, cancellationToken: ct));
        }

        await tx.CommitAsync(ct);
        return id;
    }
    public async Task<Shift?> GetOpenShiftAsync(Guid branchId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var shift = await connection.QuerySingleOrDefaultAsync<ShiftRow>(new CommandDefinition(
            ShiftSelect + " WHERE s.branch_id = @branchId AND s.status = 'open'",
            new { branchId }, cancellationToken: ct));
        return shift is null ? null : await MaterializeAsync(connection, shift, ct);
    }

    public async Task<Shift?> GetShiftAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var shift = await connection.QuerySingleOrDefaultAsync<ShiftRow>(new CommandDefinition(
            ShiftSelect + " WHERE s.id = @id", new { id }, cancellationToken: ct));
        return shift is null ? null : await MaterializeAsync(connection, shift, ct);
    }

    private static async Task<Shift> MaterializeAsync(NpgsqlConnection connection, ShiftRow row,
        CancellationToken ct)
    {
        var shift = Map(row);
        shift.Denominations = (await ListDenominationsAsync(connection, shift.Id, ct)).ToList();

        // أرصدة الخزائن المتابَعة في الشفت (كل خزينة عمود مستقل) — 0097.
        shift.TreasuryBalances = (await connection.QueryAsync<ShiftTreasuryBalance>(
            new CommandDefinition("""
            SELECT t.id AS TreasuryId, t.code AS Code, t.name_ar AS NameAr,
                   COALESCE(c.counted_cash, c.expected_cash, 0) AS Balance
            FROM shift_cash_counts c
            JOIN treasuries t ON t.id = c.treasury_id
            WHERE c.shift_id = @shiftId
            ORDER BY t.code
            """, new { shiftId = shift.Id }, cancellationToken: ct))).ToList();

        // عدادات أجهزة الشفت (كل جهاز عمود مستقل) — 0097.
        shift.MeterBalances = (await connection.QueryAsync<ShiftMeterBalance>(
            new CommandDefinition("""
            SELECT a.id AS AssetId, a.code AS Code, a.name_ar AS NameAr,
                   COALESCE((SELECT r.value FROM asset_meter_readings r
                             WHERE r.asset_id = a.id
                               AND (r.created_at >= @openedAt)
                               AND (@closedAt::timestamptz IS NULL OR r.created_at <= @closedAt)
                             ORDER BY r.created_at DESC LIMIT 1),
                            a.current_meter_value, 0) AS Value
            FROM fixed_assets a
            WHERE a.branch_id = @branchId AND a.is_metered AND a.status <> 'DISPOSED'
            ORDER BY a.code
            """, new { shiftId = shift.Id, openedAt = shift.OpenedAt, closedAt = shift.ClosedAt,
                branchId = shift.BranchId }, cancellationToken: ct))).ToList();

        return shift;
    }

    private static Shift Map(ShiftRow row) => new()
    {
        Id = row.Id, BranchId = row.BranchId, BranchName = row.BranchName,
        TreasuryId = row.TreasuryId, TreasuryCode = row.TreasuryCode,
        TreasuryName = row.TreasuryName, UserId = row.UserId, Username = row.Username,
        Code = row.Code, OpenedAt = row.OpenedAt, ClosedAt = row.ClosedAt,
        ClosedBy = row.ClosedBy, ClosedByName = row.ClosedByName,
        OpeningCash = row.OpeningCash, CarriedBalance = row.CarriedBalance,
        ReviewBalance = row.ReviewBalance, MeterBalance = row.MeterBalance,
        ExpectedCash = row.ExpectedCash, CountedCash = row.CountedCash, Variance = row.Variance,
        ReceiptsTotal = row.ReceiptsTotal, TreasuryBalance = row.TreasuryBalance,
        Status = row.Status, Notes = row.Notes, ReopenedAt = row.ReopenedAt,
        ReopenReason = row.ReopenReason, ReviewStatus = row.ReviewStatus,
        AutoClosed = row.AutoClosed, ReviewedAt = row.ReviewedAt
    };

    public async Task<IReadOnlyList<Shift>> ListShiftsAsync(Guid? branchId, DateTime? from,
        DateTime? to, int limit, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<ShiftRow>(new CommandDefinition(
            ShiftSelect + """
             WHERE (@branchId::uuid IS NULL OR s.branch_id = @branchId::uuid)
               AND (@from::date IS NULL OR s.opened_at::date >= @from::date)
               AND (@to::date IS NULL OR s.opened_at::date <= @to::date)
             ORDER BY s.opened_at DESC, s.id DESC
             LIMIT @limit
            """, new { branchId, from = from?.Date, to = to?.Date,
                limit = Math.Clamp(limit, 1, 500) }, cancellationToken: ct));
        // تحميل تفاصيل كل شفت (أرصدة الخزائن + عدادات الأجهزة) — 0097.
        var result = new List<Shift>();
        foreach (var row in rows) result.Add(await MaterializeAsync(connection, row, ct));
        return result;
    }

    public async Task<IReadOnlyList<Shift>> ListShiftsDueForAutoCloseAsync(
        DateTimeOffset cutoff, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<ShiftRow>(new CommandDefinition(
            ShiftSelect + " WHERE s.status = 'open' AND s.opened_at < @cutoff",
            new { cutoff = cutoff.ToUniversalTime() }, cancellationToken: ct));
        return rows.Select(Map).ToList();
    }

    public async Task<bool> HasOpenShiftAsync(Guid userId, Guid? branchId,
        IReadOnlyCollection<Guid> branchIds = null!, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition("""
            SELECT EXISTS (
                SELECT 1 FROM cashier_shifts s
                WHERE s.status = 'open'
                  AND (s.user_id = @userId
                       OR (@branchId::uuid IS NOT NULL AND s.branch_id = @branchId::uuid)
                       OR (coalesce(array_length(@branchIds::uuid[], 1), 0) > 0
                           AND s.branch_id = ANY(@branchIds::uuid[]))))
            """, new
        {
            userId, branchId,
            branchIds = (branchIds ?? Array.Empty<Guid>()).ToArray()
        }, cancellationToken: ct));
    }

    /// <summary>حفظ جدول الفئات النقدية للشفت المفتوحة (يستبدل السابق).</summary>
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

