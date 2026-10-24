using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class TreasuryRepository
{

    /// <summary>
    /// إنشاء سند مسودة: الرأس والسطور في معاملة واحدة. الرسوم تُحسب من إعدادات
    /// طريقة الدفع ما لم تُمرَّر صراحةً، والترقيم يحدث عند الترحيل فقط.
    /// </summary>
    public async Task<Guid> CreateVoucherAsync(Guid id, VoucherInput input, Guid userId,
        CancellationToken ct = default)
    {
        if (!VoucherTypes.IsValid(input.VoucherType))
            throw new ValidationAppException("validation.voucher_type");
        if (input.Lines.Count == 0)
            throw new ValidationAppException("validation.document_has_no_lines");

        var isTransfer = input.VoucherType == VoucherTypes.Transfer;
        var partyType = isTransfer ? null
            : input.VoucherType == VoucherTypes.Receipt ? "C" : "S";
        if (!isTransfer && (input.PartyId is null || string.IsNullOrWhiteSpace(input.PartyName)))
            throw new ValidationAppException("validation.party_required");
        if (isTransfer && input.ToTreasuryId is null)
            throw new ValidationAppException("validation.transfer_target_required");
        if (isTransfer && input.ToTreasuryId == input.TreasuryId)
            throw new ValidationAppException("validation.transfer_same_treasury");

        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var treasury = await GetTreasuryRowAsync(connection, tx, input.TreasuryId, ct)
            ?? throw new ValidationAppException("validation.treasury_not_found");
        if (treasury.BranchId != input.BranchId)
            throw new ValidationAppException("validation.treasury_branch_mismatch");

        if (isTransfer)
        {
            var target = await GetTreasuryRowAsync(connection, tx, input.ToTreasuryId!.Value, ct)
                ?? throw new ValidationAppException("validation.treasury_not_found");
            if (target.Id == treasury.Id)
                throw new ValidationAppException("validation.transfer_same_treasury");
        }

        var methods = (await connection.QueryAsync<MethodRow>(new CommandDefinition("""
            SELECT id AS Id, code AS Code, method_type AS MethodType,
                   clearing_account_id AS ClearingAccountId, fee_percent AS FeePercent,
                   fee_fixed AS FeeFixed, requires_reference AS RequiresReference
            FROM treasury_methods WHERE treasury_id = @treasuryId AND is_active
            """, new { treasuryId = input.TreasuryId }, tx, cancellationToken: ct)))
            .ToDictionary(m => m.Id);

        var lines = new List<(Guid MethodId, decimal Amount, decimal Fee, string? Reference)>();
        decimal total = 0, feeTotal = 0;
        foreach (var line in input.Lines)
        {
            if (line.Amount <= 0)
                throw new ValidationAppException("validation.voucher_amount");
            if (!methods.TryGetValue(line.TreasuryMethodId, out var method))
                throw new ValidationAppException("validation.treasury_method_not_found");
            if (method.RequiresReference && string.IsNullOrWhiteSpace(line.Reference))
                throw new ValidationAppException("validation.reference_required");

            var fee = Math.Round(
                line.FeeAmount ?? line.Amount * method.FeePercent / 100m + method.FeeFixed,
                2, MidpointRounding.AwayFromZero);
            if (fee < 0 || fee >= line.Amount)
                throw new ValidationAppException("validation.invalid_fee");

            lines.Add((method.Id, line.Amount, fee, line.Reference?.Trim()));
            total += line.Amount;
            feeTotal += fee;
        }

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO vouchers (id, branch_id, doc_date, status, voucher_type, party_type, party_id,
                party_name, treasury_id, to_treasury_id, total_amount, fee_total, notes, created_by)
            VALUES (@id, @BranchId, @docDate, 'draft', @VoucherType, @partyType, @PartyId,
                    @PartyName, @TreasuryId, @ToTreasuryId, @total, @feeTotal, @Notes, @userId)
            """, new
        {
            id, input.BranchId, docDate = input.DocDate.Date, input.VoucherType, partyType,
            input.PartyId, input.PartyName, input.TreasuryId, input.ToTreasuryId, total, feeTotal,
            input.Notes, userId
        }, tx, cancellationToken: ct));

        var lineNo = 0;
        foreach (var line in lines)
        {
            lineNo++;
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO voucher_lines (voucher_id, line_no, treasury_method_id, amount,
                    fee_amount, reference)
                VALUES (@id, @lineNo, @methodId, @amount, @fee, @reference)
                """, new
            {
                id, lineNo, methodId = line.MethodId, amount = line.Amount, fee = line.Fee,
                reference = line.Reference
            }, tx, cancellationToken: ct));
        }

        await tx.CommitAsync(ct);
        return id;
    }
    public async Task<Voucher?> GetVoucherAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var voucher = await connection.QuerySingleOrDefaultAsync<Voucher>(new CommandDefinition(
            VoucherHeadSelect + " WHERE v.id = @id", new { id }, cancellationToken: ct));
        if (voucher is null) return null;

        await AttachLinesAsync(connection, new[] { voucher }, ct);
        return voucher;
    }

    public async Task<IReadOnlyList<Voucher>> ListVouchersAsync(Guid? branchId, string? voucherType,
        string? status, int limit, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var vouchers = (await connection.QueryAsync<Voucher>(new CommandDefinition(
            VoucherHeadSelect + """
             WHERE (@branchId::uuid IS NULL OR v.branch_id = @branchId::uuid)
               AND (@voucherType::varchar IS NULL OR v.voucher_type = @voucherType::varchar)
               AND (@status::varchar IS NULL OR v.status = @status::varchar)
             ORDER BY v.doc_date DESC, v.id DESC
             LIMIT @limit
            """, new { branchId, voucherType, status, limit = Math.Clamp(limit, 1, 500) },
            cancellationToken: ct))).ToList();

        await AttachLinesAsync(connection, vouchers, ct);
        return vouchers;
    }

    public async Task<bool> DeleteVoucherAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM vouchers WHERE id = @id AND status = 'draft'", new { id },
            cancellationToken: ct)) > 0;
    }

    /// <summary>تخصيص دفعة (سند مرحّل) على فاتورة مرحّلة مع منع تجاوز مبلغ السند.</summary>
    public async Task<Guid> AllocateAsync(Guid voucherId, Guid documentId, decimal amount,
        Guid userId, CancellationToken ct = default)
    {
        amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        if (amount <= 0)
            throw new ValidationAppException("validation.allocation_amount");

        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var voucher = await connection.QuerySingleOrDefaultAsync<Voucher>(new CommandDefinition(
            "SELECT id AS Id, status AS Status, voucher_type AS VoucherType, " +
            "total_amount AS TotalAmount, doc_no AS DocNo, branch_id AS BranchId, " +
            "party_type::text AS PartyType, party_id AS PartyId " +
            "FROM vouchers WHERE id = @voucherId FOR UPDATE",
            new { voucherId }, tx, cancellationToken: ct)) ?? throw new NotFoundAppException();

        if (voucher.Status != VoucherStatuses.Posted)
            throw new ValidationAppException("validation.voucher_not_posted");
        if (voucher.VoucherType == VoucherTypes.Transfer)
            throw new ValidationAppException("validation.allocation_not_allowed");

        var document = await connection.QuerySingleOrDefaultAsync<AllocationTarget>(
            new CommandDefinition("""
            SELECT id AS Id, status AS Status, doc_no AS DocNo, total AS Total,
                   allocated_total AS AllocatedTotal
            FROM commercial_documents WHERE id = @documentId FOR UPDATE
            """, new { documentId }, tx, cancellationToken: ct));
        if (document is null || document.Status != CommercialStatuses.Posted)
            throw new ValidationAppException("validation.document_not_posted");
        if (document.AllocatedTotal + amount > document.Total)
            throw new ValidationAppException("validation.allocation_exceeds_document");

        var duplicate = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM document_allocations " +
            "WHERE source_voucher_id = @voucherId AND target_document_id = @documentId)",
            new { voucherId, documentId }, tx, cancellationToken: ct));
        if (duplicate)
            throw new ValidationAppException("validation.allocation_duplicate");

        var allocated = await connection.ExecuteScalarAsync<decimal>(new CommandDefinition(
            "SELECT COALESCE(SUM(amount), 0) FROM document_allocations " +
            "WHERE source_voucher_id = @voucherId", new { voucherId }, tx,
            cancellationToken: ct));
        if (allocated + amount > voucher.TotalAmount)
            throw new ValidationAppException("validation.allocation_exceeds_voucher");

        var id = Guid.CreateVersion7();
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO document_allocations (id, source_voucher_id, target_document_id, amount,
                created_by)
            VALUES (@id, @voucherId, @documentId, @amount, @userId)
            """, new { id, voucherId, documentId, amount, userId }, tx, cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE commercial_documents SET allocated_total = allocated_total + @amount " +
            "WHERE id = @documentId", new { amount, documentId }, tx, cancellationToken: ct));

        await CreateSettlementEntryAsync(connection, tx, voucher, document, amount, userId, ct);

        await tx.CommitAsync(ct);
        return id;
    }

    public async Task<IReadOnlyList<VoucherAllocation>> ListAllocationsAsync(Guid voucherId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return (await connection.QueryAsync<VoucherAllocation>(new CommandDefinition("""
            SELECT a.id AS Id, a.source_voucher_id AS SourceVoucherId,
                   a.target_document_id AS TargetDocumentId, d.doc_no AS TargetDocNo,
                   d.doc_date AS TargetDate, a.amount AS Amount
            FROM document_allocations a
            JOIN commercial_documents d ON d.id = a.target_document_id
            WHERE a.source_voucher_id = @voucherId
            ORDER BY d.doc_date, d.id
            """, new { voucherId }, cancellationToken: ct))).ToList();
    }

    /// <summary>إلغاء مسودة سند فقط؛ السند المرحّل لا يُلغى ولا يُحذف (G8).</summary>
    public async Task<bool> CancelVoucherAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE vouchers SET status = 'cancelled' WHERE id = @id AND status = 'draft'",
            new { id }, cancellationToken: ct)) > 0;
    }

}
