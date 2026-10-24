using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class TreasuryRepository
{
    private sealed class VoucherPostHead
    {
        public Guid Id { get; set; }
        public string VoucherType { get; set; } = VoucherTypes.Receipt;
        public Guid BranchId { get; set; }
        public DateTime DocDate { get; set; }
        public string Status { get; set; } = VoucherStatuses.Draft;
        public string? DocNo { get; set; }
        public Guid TreasuryId { get; set; }
        public Guid? ToTreasuryId { get; set; }
        public string? PartyType { get; set; }
        public Guid? PartyId { get; set; }
        public string? PartyName { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal FeeTotal { get; set; }
        public Guid? JournalEntryId { get; set; }
    }

    private sealed class VoucherPostLine
    {
        public int LineNo { get; set; }
        public Guid TreasuryMethodId { get; set; }
        public decimal Amount { get; set; }
        public decimal FeeAmount { get; set; }
        public string? Reference { get; set; }
        public string MethodType { get; set; } = PaymentMethodTypes.Cash;
        public Guid? ClearingAccountId { get; set; }
    }

    private sealed record JournalLine(Guid AccountId, decimal Debit, decimal Credit,
        Guid? CustomerId = null, Guid? SupplierId = null);

    private static string Label(string voucherType) => voucherType switch
    {
        VoucherTypes.Payment => "صرف",
        VoucherTypes.Transfer => "تحويل",
        _ => "قبض"
    };
    /// <summary>
    /// ترحيل سند في معاملة واحدة: فحص الفترة، ثم قيد الأستاذ:
    /// قبض = Dr خزينة/وسيط + Dr مصاريف / Cr دفعات العملاء،
    /// صرف = Dr دفعات الموردين + Dr مصاريف / Cr خزينة/وسيط،
    /// تحويل = Dr الوجهة / Cr المصدر (وبين الفروع عبر حساب التسوية).
    /// </summary>
    public async Task<Guid> PostVoucherAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var head = await connection.QuerySingleOrDefaultAsync<VoucherPostHead>(new CommandDefinition("""
            SELECT id AS Id, voucher_type AS VoucherType, branch_id AS BranchId,
                   doc_date AS DocDate, status AS Status, doc_no AS DocNo, treasury_id AS TreasuryId,
                   to_treasury_id AS ToTreasuryId, party_type::text AS PartyType, party_id AS PartyId,
                   party_name AS PartyName, total_amount AS TotalAmount, fee_total AS FeeTotal,
                   journal_entry_id AS JournalEntryId
            FROM vouchers WHERE id = @id FOR UPDATE
            """, new { id }, tx, cancellationToken: ct)) ?? throw new NotFoundAppException();

        if (head.Status != VoucherStatuses.Draft || head.JournalEntryId is not null)
            throw new ValidationAppException("validation.voucher_not_draft");

        var lines = (await connection.QueryAsync<VoucherPostLine>(new CommandDefinition("""
            SELECT l.line_no AS LineNo, l.treasury_method_id AS TreasuryMethodId,
                   l.amount AS Amount, l.fee_amount AS FeeAmount, l.reference AS Reference,
                   m.method_type AS MethodType, m.clearing_account_id AS ClearingAccountId
            FROM voucher_lines l
            JOIN treasury_methods m ON m.id = l.treasury_method_id
            WHERE l.voucher_id = @id ORDER BY l.line_no
            """, new { id }, tx, cancellationToken: ct))).ToList();
        if (lines.Count == 0)
            throw new ValidationAppException("validation.document_has_no_lines");

        var source = await GetTreasuryRowAsync(connection, tx, head.TreasuryId, ct)
            ?? throw new ValidationAppException("validation.treasury_not_found");
        var target = head.ToTreasuryId is null ? null
            : await GetTreasuryRowAsync(connection, tx, head.ToTreasuryId.Value, ct)
              ?? throw new ValidationAppException("validation.treasury_not_found");

        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT fn_assert_period_open(@docDate::date, @branchId, 'ACCOUNTING')",
            new { docDate = head.DocDate, branchId = head.BranchId }, tx, cancellationToken: ct));

        var defaults = (await connection.QueryAsync<(string Key, Guid? AccountId)>(
            new CommandDefinition("SELECT setting_key, account_id FROM posting_defaults",
                transaction: tx, cancellationToken: ct)))
            .Where(r => r.AccountId.HasValue)
            .ToDictionary(r => r.Key, r => r.AccountId!.Value, StringComparer.OrdinalIgnoreCase);

        Guid Need(string key)
        {
            if (defaults.TryGetValue(key, out var account)) return account;
            throw new ValidationAppException("validation.posting_account_missing",
                new Dictionary<string, string> { ["key"] = key, ["module"] = "treasury" });
        }

        var interBranch = target is not null && target.BranchId != head.BranchId;
        var gl = BuildVoucherGl(head, lines, source, target, interBranch, Need);

        var docNo = await connection.ExecuteScalarAsync<string>(new CommandDefinition("""
            SELECT next_document_number(@docType::varchar, @branchId::uuid, @docDate::date,
                                        NULL::varchar, 6::smallint)
            """, new
        {
            docType = VoucherTypes.SequenceName(head.VoucherType),
            branchId = head.BranchId, docDate = head.DocDate
        }, tx, cancellationToken: ct));

        var entryId = Guid.CreateVersion7();
        await CreateVoucherEntryAsync(connection, tx, entryId, source.TreasuryType, head.BranchId,
            head.DocDate, docNo, $"سند {Label(head.VoucherType)} {docNo}", gl, userId, head.Id, ct);

        if (interBranch)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "SELECT fn_assert_period_open(@docDate::date, @branchId, 'ACCOUNTING')",
                new { docDate = head.DocDate, branchId = target!.BranchId }, tx,
                cancellationToken: ct));

            var destinationGl = new List<JournalLine>
            {
                new(target!.AccountId, head.TotalAmount, 0),
                new(Need(PostingKeys.InterBranchClearing), 0, head.TotalAmount)
            };
            await CreateVoucherEntryAsync(connection, tx, Guid.CreateVersion7(), target.TreasuryType,
                target.BranchId, head.DocDate, docNo,
                $"استلام تحويل بين الفروع {docNo}", destinationGl, userId, null, ct);
        }

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE vouchers SET status = 'posted', doc_no = @docNo, posted_at = now(),
                posted_by = @userId, journal_entry_id = @entryId, updated_by = @userId
            WHERE id = @id
            """, new { docNo, userId, entryId, id }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return id;
    }

    private static List<JournalLine> BuildVoucherGl(VoucherPostHead head,
        IReadOnlyList<VoucherPostLine> lines, TreasuryRow source, TreasuryRow? target,
        bool interBranch, Func<string, Guid> need)
    {
        var gl = new List<JournalLine>();

        void Add(Guid account, decimal debit, decimal credit, bool asCustomer = false,
            bool asSupplier = false) =>
            gl.Add(new JournalLine(account, debit, credit,
                asCustomer ? head.PartyId : null, asSupplier ? head.PartyId : null));

        switch (head.VoucherType)
        {
            case VoucherTypes.Receipt:
                foreach (var line in lines)
                    Add(line.ClearingAccountId ?? source.AccountId, line.Amount - line.FeeAmount, 0);
                if (head.FeeTotal > 0) Add(need(PostingKeys.PaymentFees), head.FeeTotal, 0);
                Add(need(PostingKeys.CustomerAdvances), 0, head.TotalAmount, asCustomer: true);
                break;

            case VoucherTypes.Payment:
                Add(need(PostingKeys.SupplierAdvances), head.TotalAmount, 0, asSupplier: true);
                if (head.FeeTotal > 0) Add(need(PostingKeys.PaymentFees), head.FeeTotal, 0);
                foreach (var line in lines)
                    Add(line.ClearingAccountId ?? source.AccountId, 0, line.Amount - line.FeeAmount);
                break;

            case VoucherTypes.Transfer when target is not null:
                if (interBranch) Add(need(PostingKeys.InterBranchClearing), head.TotalAmount, 0);
                else Add(target.AccountId, head.TotalAmount, 0);
                if (head.FeeTotal > 0) Add(need(PostingKeys.PaymentFees), head.FeeTotal, 0);
                Add(source.AccountId, 0, head.TotalAmount + head.FeeTotal);
                break;

            default:
                throw new ValidationAppException("validation.voucher_type");
        }

        return gl;
    }

    /// <summary>قيد مسودة + بنود + ترحيل ذري عبر fn_gl_post_entry.</summary>
    private static async Task CreateVoucherEntryAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, Guid entryId, string treasuryType, Guid branchId, DateTime docDate,
        string docNo, string description, IReadOnlyList<JournalLine> gl, Guid userId,
        Guid? sourceId, CancellationToken ct, string sourceType = "voucher")
    {
        var debit = gl.Sum(x => x.Debit);
        var credit = gl.Sum(x => x.Credit);
        if (Math.Round(debit - credit, 2, MidpointRounding.AwayFromZero) != 0)
            throw new ValidationAppException("validation.entry_not_balanced");

        var journalType = treasuryType == TreasuryTypes.Cash ? "cash" : "bank";
        var journalId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT id FROM journals WHERE journal_type = @journalType AND is_active " +
            "ORDER BY sort_order LIMIT 1", new { journalType }, tx, cancellationToken: ct))
            ?? throw new ValidationAppException("validation.journal_not_found");

        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO journal_entries (id, journal_id, branch_id, entry_date, reference,
                description, status, source_type, source_id, total_debit, total_credit, created_by)
            VALUES (@entryId, @journalId, @branchId, @docDate, @docNo, @description, 'draft',
                    @sourceType, @sourceId, @debit, @credit, @userId)
            """, new
        {
            entryId, journalId, branchId, docDate, docNo, description, sourceType, sourceId,
            debit, credit, userId
        }, tx, cancellationToken: ct));

        var lineNo = 0;
        foreach (var line in gl)
        {
            lineNo++;
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO journal_entry_lines (id, entry_id, line_no, account_id, description,
                    debit, credit, customer_id, supplier_id, branch_id, entry_date)
                VALUES (@lineId, @entryId, @lineNo, @accountId, @description, @debit, @credit,
                        @customerId, @supplierId, @branchId, @docDate)
                """, new
            {
                lineId = Guid.CreateVersion7(), entryId, lineNo, accountId = line.AccountId,
                description, debit = line.Debit, credit = line.Credit,
                customerId = line.CustomerId, supplierId = line.SupplierId, branchId, docDate
            }, tx, cancellationToken: ct));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT fn_gl_post_entry(@entryId, @userId)", new { entryId, userId }, tx,
            cancellationToken: ct));
    }

}
