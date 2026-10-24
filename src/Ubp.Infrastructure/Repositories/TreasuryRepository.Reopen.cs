using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class TreasuryRepository
{
    /// <summary>
    /// إعادة فتح وردية مغلقة (قرار مدير + سبب): عكس قيد الفرق إن وُجد، ثم إرجاعها
    /// إلى الحالة 'open' مع أثر تدقيقي، فيبقى قيد «وردية واحدة مفتوحة» سارياً.
    /// </summary>
    public async Task<Shift> ReopenShiftAsync(Guid shiftId, string reason, Guid userId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ValidationAppException("validation.shift_reason_required");

        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var shift = await connection.QuerySingleOrDefaultAsync<ShiftRow>(new CommandDefinition(
            ShiftSelect + " WHERE s.id = @shiftId FOR UPDATE OF s", new { shiftId }, tx,
            cancellationToken: ct)) ?? throw new NotFoundAppException();

        if (shift.Status == ShiftStatuses.Open)
            throw new ValidationAppException("validation.shift_already_open");
        if (shift.Status != ShiftStatuses.Closed)
            throw new ValidationAppException("validation.shift_not_closed");

        var entryId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
            SELECT id FROM journal_entries
            WHERE source_type = 'cash_shift' AND reference = @code
              AND reversal_of_id IS NULL AND NOT is_reversed
            """, new { code = shift.Code }, tx, cancellationToken: ct));
        if (entryId is not null)
            await connection.ExecuteAsync(new CommandDefinition(
                "SELECT fn_gl_reverse_entry(@entryId, @userId, @date::date)",
                new { entryId, userId, date = DateTime.Today }, tx, cancellationToken: ct));

        try
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                UPDATE cashier_shifts SET status = 'open', closed_at = NULL, counted_cash = NULL,
                    variance = NULL, closed_by = NULL, reopened_by = @userId, reopened_at = now(),
                    reopen_reason = @reason, updated_by = @userId
                WHERE id = @shiftId
                """, new { userId, reason = reason.Trim(), shiftId }, tx, cancellationToken: ct));
        }
        catch (PostgresException ex) when (ex.ConstraintName == "ux_shift_open")
        {
            throw new ValidationAppException("validation.shift_already_open");
        }

        await tx.CommitAsync(ct);
        return (await GetShiftAsync(shiftId, ct))!;
    }
}
