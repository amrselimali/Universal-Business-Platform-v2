using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class BookingCollectionRepository
{
    private sealed record PayRow(Guid TreasuryId, Guid MethodId, decimal Amount, decimal Fee,
        decimal Collected, Guid AccountId, string TreasuryType, Guid? ClearingAccountId,
        Guid? FeeAccountId);

    public async Task<BookingCollectionResult> CollectAsync(Guid voucherId,
        BookingCollectionInput input, Guid userId, CancellationToken ct = default)
    {
        if (input.BranchId == Guid.Empty || input.CustomerId == Guid.Empty)
            throw new ValidationAppException("validation.required_fields");

        var lines = input.Lines.Where(l => l.Amount > 0).ToList();
        if (lines.Count == 0)
            throw new ValidationAppException("validation.document_has_no_lines");
        if (lines.Select(l => l.BookingId).Distinct().Count() != lines.Count)
            throw new ValidationAppException("validation.booking_duplicate_line");

        var bookingTotal = Math.Round(lines.Sum(l => l.Amount), 2, MidpointRounding.AwayFromZero);
        if (bookingTotal <= 0) throw new ValidationAppException("validation.voucher_amount");

        var payments = input.Payments.Where(p => p.Amount > 0).ToList();
        if (payments.Count == 0 && input.TreasuryId != Guid.Empty &&
            input.TreasuryMethodId != Guid.Empty)
            payments.Add(new PosPaymentInput
            {
                TreasuryId = input.TreasuryId, TreasuryMethodId = input.TreasuryMethodId,
                Amount = bookingTotal
            });
        if (payments.Count == 0)
            throw new ValidationAppException("validation.required_fields");
        if (payments.Select(p => p.TreasuryMethodId).Distinct().Count() != payments.Count)
            throw new ValidationAppException("validation.payment_duplicate_method");

        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var methodIds = payments.Select(p => p.TreasuryMethodId).Distinct().ToArray();
        var methods = (await connection.QueryAsync<(Guid Id, Guid TreasuryId, Guid AccountId,
            string TreasuryType, Guid? ClearingAccountId, Guid? FeeAccountId, decimal FeePercent,
            decimal FeeFixed)>(new CommandDefinition("""
            SELECT m.id AS Id, t.id AS TreasuryId, t.account_id AS AccountId,
                   t.treasury_type AS TreasuryType, m.clearing_account_id AS ClearingAccountId,
                   m.fee_account_id AS FeeAccountId, m.fee_percent AS FeePercent,
                   m.fee_fixed AS FeeFixed
            FROM treasury_methods m
            JOIN treasuries t ON t.id = m.treasury_id
            WHERE m.id = ANY(@ids) AND t.branch_id = @branchId
              AND t.is_active AND m.is_active
            """, new { ids = methodIds, branchId = input.BranchId }, tx, cancellationToken: ct)))
            .ToDictionary(x => x.Id);
        if (methods.Count != methodIds.Length)
            throw new ValidationAppException("validation.treasury_method_not_found");

        var payRows = new List<PayRow>();
        foreach (var payment in payments)
        {
            var m = methods[payment.TreasuryMethodId];
            var fee = Math.Round(payment.Amount * m.FeePercent / 100m + m.FeeFixed, 2,
                MidpointRounding.AwayFromZero);
            if (fee < 0 || fee >= payment.Amount)
                throw new ValidationAppException("validation.invalid_fee");
            payRows.Add(new PayRow(m.TreasuryId, m.Id, payment.Amount, fee,
                payment.Amount - fee, m.AccountId, m.TreasuryType, m.ClearingAccountId,
                m.FeeAccountId));
        }

        var feeTotal = Math.Round(payRows.Sum(r => r.Fee), 2, MidpointRounding.AwayFromZero);
        var collectedTotal = Math.Round(payRows.Sum(r => r.Collected), 2,
            MidpointRounding.AwayFromZero);
        if (collectedTotal > bookingTotal)
            throw new ValidationAppException("validation.collection_exceeds_selected");

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
        var vouchers = new List<(Guid Id, string No, decimal Collected)>();
        foreach (var group in payRows.GroupBy(r => r.TreasuryId))
        {
            var rows = group.ToList();
            var groupGross = Math.Round(rows.Sum(r => r.Amount), 2, MidpointRounding.AwayFromZero);
            var groupFee = Math.Round(rows.Sum(r => r.Fee), 2, MidpointRounding.AwayFromZero);
            var groupCollected = Math.Round(rows.Sum(r => r.Collected), 2,
                MidpointRounding.AwayFromZero);

            var no = await connection.ExecuteScalarAsync<string>(new CommandDefinition("""
                SELECT next_document_number('RCV'::varchar, @branchId::uuid, @docDate::date,
                                            'RCV'::varchar, 6::smallint)
                """, new { branchId = input.BranchId, docDate }, tx, cancellationToken: ct))!;

            var id = Guid.CreateVersion7();
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO vouchers (id, branch_id, doc_date, status, voucher_type, party_type,
                    party_id, party_name, treasury_id, total_amount, fee_total, notes, created_by)
                VALUES (@id, @branchId, @docDate, 'draft', 'receipt', 'C', @customerId,
                        (SELECT name_ar FROM customers WHERE id = @customerId), @treasuryId,
                        @groupGross, @groupFee, @notes, @userId)
                """, new { id, branchId = input.BranchId, docDate,
                    customerId = input.CustomerId, treasuryId = group.Key, groupGross,
                    groupFee, notes = input.Notes, userId }, tx, cancellationToken: ct));

            var lineNo = 0;
            var gl = new List<EntryLine>();
            foreach (var row in rows)
            {
                lineNo++;
                await connection.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO voucher_lines (voucher_id, line_no, treasury_method_id, amount,
                        fee_amount, reference)
                    VALUES (@id, @lineNo, @methodId, @amount, @fee, NULL)
                    """, new { id, lineNo, methodId = row.MethodId, amount = row.Amount,
                        fee = row.Fee }, tx, cancellationToken: ct));

                gl.Add(new EntryLine(row.ClearingAccountId ?? row.AccountId, row.Collected, 0));
                if (row.Fee > 0)
                    gl.Add(new EntryLine(row.FeeAccountId ??
                        await NeedAsync(connection, tx, PostingKeys.PaymentFees, ct), row.Fee, 0));
            }
            gl.Add(new EntryLine(await NeedAsync(connection, tx, PostingKeys.CustomerAdvances, ct),
                0, groupGross, input.CustomerId));

            if (!input.DraftOnly)
            {
                var entryId = await PostEntryAsync(connection, tx,
                    rows[0].TreasuryType == TreasuryTypes.Cash ? "cash" : "bank", input.BranchId,
                    docDate, no, $"تحصيل خدمات محجوزة {no}", gl, userId, "voucher", id, ct);

                await connection.ExecuteAsync(new CommandDefinition("""
                    UPDATE vouchers SET status = 'posted', doc_no = @no, posted_at = now(),
                        posted_by = @userId, journal_entry_id = @entryId WHERE id = @id
                    """, new { no, userId, entryId, id }, tx, cancellationToken: ct));
            }
            vouchers.Add((id, input.DraftOnly ? null : no, groupCollected));
        }

        // ترحيل المسودة لا يخصّص أي مبلغ (لا أثر على الحسابات).
        if (input.DraftOnly)
        {
            await tx.CommitAsync(ct);
            return new BookingCollectionResult
            {
                VoucherId = vouchers[0].Id, VoucherNo = null, Total = collectedTotal,
                Fee = feeTotal, Bookings = lines.Count
            };
        }

        // توزيع المبلغ المحصل (قد يكون جزئيًا) على الحجوزات المحددة بحدود المتبقي لكل حجز.
        var voucherIndex = 0;
        var voucherRemaining = vouchers[0].Collected;
        var collectedRemaining = collectedTotal;
        foreach (var line in lines)
        {
            if (collectedRemaining <= 0) break;
            var cap = Math.Round(line.Amount, 2, MidpointRounding.AwayFromZero);
            var amount = Math.Min(cap, collectedRemaining);
            collectedRemaining = Math.Round(collectedRemaining - amount, 2,
                MidpointRounding.AwayFromZero);
            var isInvoice = string.Equals(line.Kind, "invoice", StringComparison.OrdinalIgnoreCase);
            while (amount > 0)
            {
                var take = Math.Min(amount, voucherRemaining);
                var allocationVoucherId = vouchers[voucherIndex].Id;
                if (isInvoice)
                {
                    // فاتورة/خدمة منفذة صارت إيراداً: تُخصَّص عليها دفعة العميل.
                    await connection.ExecuteAsync(new CommandDefinition("""
                        INSERT INTO document_allocations (source_voucher_id, target_document_id, amount)
                        VALUES (@allocationVoucherId, @documentId, @amount)
                        ON CONFLICT (source_voucher_id, target_document_id)
                        DO UPDATE SET amount = document_allocations.amount + EXCLUDED.amount
                        """, new { allocationVoucherId, documentId = line.BookingId, amount = take }, tx,
                        cancellationToken: ct));
                    await connection.ExecuteAsync(new CommandDefinition("""
                        UPDATE commercial_documents SET allocated_total = allocated_total + @take
                        WHERE id = @documentId
                        """, new { take, documentId = line.BookingId }, tx, cancellationToken: ct));
                }
                else
                {
                    await connection.ExecuteAsync(new CommandDefinition("""
                        INSERT INTO booking_payments (booking_id, voucher_id, amount, created_by, notes)
                        VALUES (@bookingId, @voucherId, @amount, @userId, @notes)
                        """, new { bookingId = line.BookingId, voucherId = allocationVoucherId,
                            amount = take, userId,
                            notes = input.Notes }, tx, cancellationToken: ct));
                }
                amount -= take;
                voucherRemaining -= take;
                if (amount > 0)
                {
                    voucherIndex++;
                    voucherRemaining = vouchers[voucherIndex].Collected;
                }
            }
        }

        await tx.CommitAsync(ct);
        return new BookingCollectionResult
        {
            VoucherId = vouchers[0].Id, VoucherNo = vouchers[0].No, Total = collectedTotal,
            Fee = feeTotal, Bookings = lines.Count
        };
    }
}
