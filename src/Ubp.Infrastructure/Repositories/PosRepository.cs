using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IPosRepository
{
    Task<PosReceipt> CheckoutAsync(PosCheckoutInput input, Guid userId, bool reserveStock,
        bool allowNegative, CancellationToken ct = default);
    /// <summary>إيصالات التحصيل للفرع مع فلتر تاريخ (يوم إلى يوم) — شاشة إيصالات POS.</summary>
    Task<IReadOnlyList<CommercialDocument>> ListReceiptsAsync(Guid? branchId, DateTime? from,
        DateTime? to, int limit, CancellationToken ct = default);
    /// <summary>رأس إيصال قبض (عميل/رقم/إجمالي/فرع) لإعادة إرسال الإشعار.</summary>
    Task<(Guid? CustomerId, string? DocNo, decimal Total, Guid BranchId)> GetReceiptHeaderAsync(
        Guid voucherId, CancellationToken ct = default);
    /// <summary>تفاصيل إيصال قبض (سطور طرق الدفع/العمولة) لعرضه أو طباعته.</summary>
    Task<Voucher?> GetReceiptDetailAsync(Guid voucherId, CancellationToken ct = default);
}

/// <summary>
/// نقطة البيع السريعة = حجز منتجات فقط (source = pos) مع تحصيل اختياري
/// بأكثر من وسيلة دفع، وقد لا يوجد تحصيل أصلاً. لا تنشئ تنفيذاً ولا فاتورة
/// ولا موعداً؛ المواعيد تُدار حصراً من شاشة المواعيد.
/// </summary>
public sealed partial class PosRepository : IPosRepository
{
    private readonly IDbConnectionFactory _factory;
    public PosRepository(IDbConnectionFactory factory) => _factory = factory;

    private sealed record EntryLine(Guid AccountId, decimal Debit, decimal Credit,
        Guid? CustomerId = null);

    private sealed class BuiltLine
    {
        public int LineNo { get; set; }
        public Guid ItemId { get; set; }
        public Guid UnitId { get; set; }
        public Guid? TaxId { get; set; }
        public string ItemType { get; set; } = "GOODS";
        public bool Direct { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal DiscountPercent { get; set; }
        public decimal NetAmount { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public string OperationType { get; set; } = ServiceOperationTypes.Normal;
        public Guid? EmployeeId { get; set; }
    }

    private sealed class PaymentAccount
    {
        public Guid TreasuryId { get; set; }
        public Guid TreasuryMethodId { get; set; }
        public Guid BranchId { get; set; }
        public string TreasuryType { get; set; } = TreasuryTypes.Cash;
        public Guid AccountId { get; set; }
        public Guid? ClearingAccountId { get; set; }
        public Guid? FeeAccountId { get; set; }
        public decimal FeePercent { get; set; }
        public decimal FeeFixed { get; set; }
        public bool RequiresReference { get; set; }
        public decimal Amount { get; set; }
        public decimal Fee { get; set; }
        public string? Reference { get; set; }
    }

    public async Task<PosReceipt> CheckoutAsync(PosCheckoutInput input, Guid userId,
        bool reserveStock, bool allowNegative, CancellationToken ct = default)
    {
        if (input.BranchId == Guid.Empty || input.CustomerId == Guid.Empty ||
            input.WarehouseId == Guid.Empty)
            throw new ValidationAppException("validation.required_fields");
        if (input.Lines.Count == 0)
            throw new ValidationAppException("validation.document_has_no_lines");

        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var items = await LoadItemInfoAsync(connection, tx, input, ct);
        var rates = await LoadTaxRatesAsync(connection, tx, input, items, ct);
        var lines = BuildLines(input, rates, items);
        var total = lines.Sum(l => l.TotalAmount);

        var payments = await ValidatePaymentsAsync(connection, tx, input, total, ct);

        var bookingId = Guid.CreateVersion7();
        var bookingNo = await NextNumberAsync(connection, tx, "BOOK", input.BranchId,
            input.DocDate, "BK", ct);
        var expiresAt = DbTime.Utc(input.ExpiresAt) ?? DateTimeOffset.UtcNow.AddMonths(6);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO bookings (id, branch_id, doc_no, booking_date, status, customer_id, source,
                due_date, starts_at, ends_at, assigned_employee_id, expires_at, total_amount,
                notes, created_by)
            VALUES (@bookingId, @BranchId, @bookingNo, @docDate, 'confirmed', @CustomerId, 'pos',
                    @docDate, NULL, NULL, NULL, @expiresAt, @total, @Notes, @userId)
            """, new { bookingId, input.BranchId, bookingNo, docDate = input.DocDate.Date,
                input.CustomerId, total, expiresAt, input.Notes,
                userId }, tx, cancellationToken: ct));

        foreach (var line in lines)
        {
            if (reserveStock && line.ItemType == "GOODS" && !line.Direct)
            {
                var available = await connection.ExecuteScalarAsync<decimal>(new CommandDefinition(
                    "SELECT COALESCE(fn_stock_available(@itemId, @warehouseId), 0)",
                    new { itemId = line.ItemId, warehouseId = input.WarehouseId }, tx,
                    cancellationToken: ct));
                if (available < line.Quantity)
                    throw new ValidationAppException("validation.booking_not_available");

                await connection.ExecuteAsync(new CommandDefinition("""
                    UPDATE stock_balances SET reserved_qty = reserved_qty + @quantity,
                        updated_at = now()
                    WHERE item_id = @itemId AND warehouse_id = @warehouseId
                    """, new { quantity = line.Quantity, itemId = line.ItemId,
                        warehouseId = input.WarehouseId }, tx, cancellationToken: ct));
            }

            // كمية العميل تُسجّل دائمًا في كارتة الحجوزات؛ حجز المخزون الفعلي يظل مشروطًا بالإعداد.
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO booking_lines (booking_id, line_no, item_id, unit_id, warehouse_id,
                    quantity, unit_price, discount_percent, tax_id, net_amount, tax_amount,
                    total_amount, reserved_qty, operation_type, employee_id)
                VALUES (@bookingId, @LineNo, @ItemId, @UnitId, @WarehouseId, @Quantity, @UnitPrice,
                        @DiscountPercent, @TaxId, @NetAmount, @TaxAmount, @TotalAmount,
                        @reserved, @OperationType, @EmployeeId)
                """, new { bookingId, line.LineNo, line.ItemId, line.UnitId, input.WarehouseId,
                    line.Quantity, line.UnitPrice, line.DiscountPercent, line.TaxId,
                    line.NetAmount, line.TaxAmount, line.TotalAmount,
                    reserved = line.Direct ? 0 : line.Quantity,
                    line.OperationType, line.EmployeeId }, tx, cancellationToken: ct));
        }

        // الأصناف «المباشرة» (أو البيع المباشر من الشاشة): تُنفَّذ الآن
        // (صرف مخزون + تكلفة + إيراد وذمم + كارتة كميات).
        await ExecuteDirectLinesAsync(connection, tx, input, bookingId, userId,
            allowNegative, ct);

        var receipt = new PosReceipt
        {
            BookingId = bookingId, BookingNo = bookingNo, ExpiresAt = expiresAt,
            Total = total, Executed = lines.Any(l => l.Direct)
        };
        foreach (var group in payments.GroupBy(p => p.TreasuryId))
        {
            var voucherId = Guid.CreateVersion7();
            var amount = group.Sum(p => p.Amount);
            var treasuryType = group.First().TreasuryType;
            var voucherNo = await NextNumberAsync(connection, tx, "RCV", input.BranchId,
                input.DocDate, "RCV", ct);

            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO vouchers (id, branch_id, doc_date, status, voucher_type, party_type,
                    party_id, party_name, treasury_id, total_amount, fee_total, created_by)
                VALUES (@voucherId, @BranchId, @docDate, 'draft', 'receipt', 'C', @CustomerId,
                        (SELECT name_ar FROM customers WHERE id = @CustomerId), @TreasuryId,
                        @amount, 0, @userId)
                """, new { voucherId, input.BranchId, docDate = input.DocDate.Date,
                    input.CustomerId, treasuryId = group.Key, amount, userId }, tx,
                cancellationToken: ct));

            var gl = new List<EntryLine>();
            var lineNo = 0;
            foreach (var payment in group)
            {
                lineNo++;
                var fee = payment.Fee;
                if (fee >= payment.Amount)
                    throw new ValidationAppException("validation.invalid_fee");

                await connection.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO voucher_lines (voucher_id, line_no, treasury_method_id, amount,
                        fee_amount, reference)
                    VALUES (@voucherId, @lineNo, @methodId, @amount, @fee, @reference)
                    """, new { voucherId, lineNo, methodId = payment.TreasuryMethodId,
                        amount = payment.Amount, fee, reference = payment.Reference }, tx,
                    cancellationToken: ct));

                gl.Add(new EntryLine(payment.ClearingAccountId ?? payment.AccountId,
                    payment.Amount - fee, 0));
                if (fee > 0)
                    gl.Add(new EntryLine(payment.FeeAccountId ??
                        await NeedAsync(connection, tx, PostingKeys.PaymentFees, ct), fee, 0));
            }

            gl.Add(new EntryLine(
                await NeedAsync(connection, tx, PostingKeys.CustomerAdvances, ct), 0, amount,
                input.CustomerId));

            if (!input.DraftOnly)
            {
                var entryId = await PostEntryAsync(connection, tx,
                    treasuryType == TreasuryTypes.Cash ? "cash" : "bank", input.BranchId,
                    input.DocDate, voucherNo, $"تحصيل POS {voucherNo}", gl, userId, "voucher",
                    voucherId, ct);

                await connection.ExecuteAsync(new CommandDefinition("""
                    UPDATE vouchers SET status = 'posted', doc_no = @voucherNo, posted_at = now(),
                        posted_by = @userId, journal_entry_id = @entryId WHERE id = @voucherId
                    """, new { voucherNo, userId, entryId, voucherId }, tx, cancellationToken: ct));
            }

            receipt.Vouchers.Add(new PosPaymentResult
            {
                VoucherId = voucherId, VoucherNo = input.DraftOnly ? null : voucherNo,
                TreasuryId = group.Key, Amount = amount, Fee = group.Sum(p => p.Fee),
                Lines = group.Count()
            });
        }

        receipt.Paid = receipt.Vouchers.Sum(v => v.Amount);
        receipt.FeeTotal = receipt.Vouchers.Sum(v => v.Fee);
        receipt.TaxTotal = lines.Sum(l => l.TaxAmount);

        // تسجيل المبالغ المحصلة على الحجز: أساس «المسدد سابقًا» في تحصيل الخدمات المحجوزة.
        // يُسجَّل حتى في «تسجيل» (مسودة) ليخفض المتبقي غير المسدد فورًا، بلا أي ترحيل محاسبي؛
        // وإلغاء/حذف المسودة يحذف هذه التخصيصات — 0095.
        foreach (var voucher in receipt.Vouchers)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO booking_payments (booking_id, voucher_id, amount, created_by, notes)
                VALUES (@bookingId, @voucherId, @amount, @userId, 'دفع مع إنشاء الحجز من نقطة البيع')
                """, new { bookingId, voucherId = voucher.VoucherId,
                    amount = voucher.Amount - voucher.Fee, userId }, tx, cancellationToken: ct));
        }

        await ConsumeServiceRecipesAsync(connection, tx, input, lines, userId, ct);

        await tx.CommitAsync(ct);
        return receipt;
    }

    private static async Task<List<PaymentAccount>> ValidatePaymentsAsync(
        NpgsqlConnection connection, NpgsqlTransaction tx, PosCheckoutInput input, decimal total,
        CancellationToken ct)
    {
        var result = new List<PaymentAccount>();
        if (input.Payments.Count == 0) return result;

        foreach (var payment in input.Payments)
        {
            if (payment.Amount <= 0)
                throw new ValidationAppException("validation.voucher_amount");

            // الخزائن هي طرق الدفع: تُستنتج طريقة الخزينة الافتراضية وعمولة مزود الخدمة — 0093.
            var methodId = payment.TreasuryMethodId == Guid.Empty
                ? (Guid?)null
                : payment.TreasuryMethodId;
            var account = await connection.QuerySingleOrDefaultAsync<PaymentAccount>(
                new CommandDefinition("""
                SELECT t.id AS TreasuryId, m.id AS TreasuryMethodId, t.branch_id AS BranchId,
                       t.treasury_type AS TreasuryType, t.account_id AS AccountId,
                       m.clearing_account_id AS ClearingAccountId,
                       t.provider_fee_account_id AS FeeAccountId,
                       t.provider_fee_percent AS FeePercent,
                       t.provider_fee_amount AS FeeFixed,
                       COALESCE(m.requires_reference, false) AS RequiresReference
                FROM treasuries t
                LEFT JOIN LATERAL (
                    SELECT mm.id, mm.clearing_account_id, mm.requires_reference
                    FROM treasury_methods mm
                    WHERE mm.treasury_id = t.id AND mm.is_active
                      AND (@methodId::uuid IS NULL OR mm.id = @methodId::uuid)
                    ORDER BY (mm.code = 'DEFAULT') DESC, mm.code
                    LIMIT 1
                ) m ON true
                WHERE t.id = @treasuryId AND t.is_active
                """, new { methodId, treasuryId = payment.TreasuryId },
                tx, cancellationToken: ct));
            if (account is null || account.AccountId == Guid.Empty ||
                account.TreasuryMethodId == Guid.Empty)
                throw new ValidationAppException("validation.treasury_method_not_found");
            if (account.BranchId != input.BranchId)
                throw new ValidationAppException("validation.treasury_branch_mismatch");
            if (account.RequiresReference && string.IsNullOrWhiteSpace(payment.Reference))
                throw new ValidationAppException("validation.reference_required");

            account.Amount = payment.Amount;
            account.Reference = payment.Reference;
            account.Fee = Math.Round(payment.Amount * account.FeePercent / 100m +
                account.FeeFixed, 2, MidpointRounding.AwayFromZero);
            payment.TreasuryMethodId = account.TreasuryMethodId;
            result.Add(account);
        }

        if (result.Sum(p => p.Amount - p.Fee) > total)
            throw new ValidationAppException("validation.pos_payment_exceeds_total");

        return result;
    }
}
