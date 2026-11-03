using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class BookingCollectionRepository
{
    public async Task<BookingCollectionResult> CollectAsync(Guid voucherId,
        BookingCollectionInput input, Guid userId, CancellationToken ct = default)
    {
        if (input.BranchId == Guid.Empty || input.CustomerId == Guid.Empty ||
            input.TreasuryId == Guid.Empty || input.TreasuryMethodId == Guid.Empty)
            throw new ValidationAppException("validation.required_fields");

        var lines = input.Lines.Where(l => l.Amount > 0).ToList();
        if (lines.Count == 0)
            throw new ValidationAppException("validation.document_has_no_lines");
        if (lines.Select(l => l.BookingId).Distinct().Count() != lines.Count)
            throw new ValidationAppException("validation.booking_duplicate_line");

        var total = Math.Round(lines.Sum(l => l.Amount), 2, MidpointRounding.AwayFromZero);
        if (total <= 0) throw new ValidationAppException("validation.voucher_amount");

        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var treasury = await connection.QuerySingleOrDefaultAsync<(Guid AccountId, string Type,
            Guid? ClearingAccountId, Guid? FeeAccountId, decimal FeePercent, decimal FeeFixed)>(
            new CommandDefinition("""
            SELECT t.account_id AS AccountId, t.treasury_type AS Type,
                   m.clearing_account_id AS ClearingAccountId,
                   m.fee_account_id AS FeeAccountId,
                   m.fee_percent AS FeePercent, m.fee_fixed AS FeeFixed
            FROM treasuries t
            JOIN treasury_methods m ON m.treasury_id = t.id
            WHERE t.id = @treasuryId AND t.branch_id = @branchId AND t.is_active
              AND m.id = @methodId AND m.is_active
            """, new { treasuryId = input.TreasuryId, branchId = input.BranchId,
                methodId = input.TreasuryMethodId }, tx, cancellationToken: ct));
        if (treasury.AccountId == Guid.Empty)
            throw new ValidationAppException("validation.treasury_method_not_found");

        var fee = Math.Round(total * treasury.FeePercent / 100m + treasury.FeeFixed, 2,
            MidpointRounding.AwayFromZero);
        if (fee < 0 || fee >= total) throw new ValidationAppException("validation.invalid_fee");

        var ids = lines.Select(l => l.BookingId).ToArray();
        var bookings = (await connection.QueryAsync<LockedBooking>(new CommandDefinition("""
            SELECT b.id AS Id, b.total_amount AS TotalAmount, b.deposit_amount AS DepositAmount,
                   COALESCE((SELECT SUM(bp.amount) FROM booking_payments bp
                             WHERE bp.booking_id = b.id), 0) AS PaidAmount
            FROM bookings b
            WHERE b.id = ANY(@ids) AND b.customer_id = @customerId
              AND b.branch_id = @branchId
              AND b.status IN ('confirmed', 'partially_delivered', 'completed')
            FOR UPDATE
            """, new { ids, customerId = input.CustomerId, branchId = input.BranchId }, tx,
            cancellationToken: ct))).ToDictionary(b => b.Id);
        if (bookings.Count != ids.Length)
            throw new ValidationAppException("validation.booking_not_found");

        foreach (var line in lines)
        {
            var booking = bookings[line.BookingId];
            var remaining = Math.Max(booking.TotalAmount - booking.DepositAmount -
                                     booking.PaidAmount, 0);
            if (line.Amount > remaining)
                throw new ValidationAppException("validation.collection_exceeds_remaining",
                    new Dictionary<string, string> { ["amount"] = remaining.ToString("N2") });
        }

        var docDate = input.DocDate.Date;
        var voucherNo = await connection.ExecuteScalarAsync<string>(new CommandDefinition("""
            SELECT next_document_number('RCV'::varchar, @branchId::uuid, @docDate::date,
                                        'RCV'::varchar, 6::smallint)
            """, new { branchId = input.BranchId, docDate }, tx, cancellationToken: ct))!;

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO vouchers (id, branch_id, doc_date, status, voucher_type, party_type,
                party_id, party_name, treasury_id, total_amount, fee_total, notes, created_by)
            VALUES (@voucherId, @branchId, @docDate, 'draft', 'receipt', 'C', @customerId,
                    (SELECT name_ar FROM customers WHERE id = @customerId), @treasuryId,
                    @total, @fee, @notes, @userId)
            """, new { voucherId, branchId = input.BranchId, docDate,
                customerId = input.CustomerId, treasuryId = input.TreasuryId, total, fee,
                notes = input.Notes, userId }, tx, cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO voucher_lines (voucher_id, line_no, treasury_method_id, amount,
                fee_amount, reference)
            VALUES (@voucherId, 1, @methodId, @total, @fee, NULL)
            """, new { voucherId, methodId = input.TreasuryMethodId, total, fee }, tx,
            cancellationToken: ct));

        var gl = new List<EntryLine>
        {
            new(treasury.ClearingAccountId ?? treasury.AccountId, total - fee, 0)
        };
        if (fee > 0)
            gl.Add(new EntryLine(treasury.FeeAccountId ??
                await NeedAsync(connection, tx, PostingKeys.PaymentFees, ct), fee, 0));
        gl.Add(new EntryLine(await NeedAsync(connection, tx, PostingKeys.CustomerAdvances, ct),
            0, total, input.CustomerId));

        var entryId = await PostEntryAsync(connection, tx,
            treasury.Type == TreasuryTypes.Cash ? "cash" : "bank", input.BranchId, docDate,
            voucherNo, $"تحصيل خدمات محجوزة {voucherNo}", gl, userId, "voucher", voucherId, ct);

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE vouchers SET status = 'posted', doc_no = @voucherNo, posted_at = now(),
                posted_by = @userId, journal_entry_id = @entryId
            WHERE id = @voucherId
            """, new { voucherNo, userId, entryId, voucherId }, tx, cancellationToken: ct));

        foreach (var line in lines)
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO booking_payments (booking_id, voucher_id, amount, created_by, notes)
                VALUES (@bookingId, @voucherId, @amount, @userId, @notes)
                """, new { bookingId = line.BookingId, voucherId,
                    amount = Math.Round(line.Amount, 2, MidpointRounding.AwayFromZero), userId,
                    notes = input.Notes }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return new BookingCollectionResult
        {
            VoucherId = voucherId, VoucherNo = voucherNo, Total = total, Fee = fee,
            Bookings = lines.Count
        };
    }
}
