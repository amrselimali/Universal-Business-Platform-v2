using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class PosRepository
{
    private sealed class ItemInfo
    {
        public Guid Id { get; set; }
        public string ItemType { get; set; } = "GOODS";
        public Guid? TaxId { get; set; }
        public bool PriceIncludesTax { get; set; }
        public decimal DefaultPrice { get; set; }
    }

    private static async Task<Dictionary<Guid, ItemInfo>> LoadItemInfoAsync(
        NpgsqlConnection connection, NpgsqlTransaction tx, PosCheckoutInput input,
        CancellationToken ct)
    {
        var ids = input.Lines.Select(l => l.ItemId).Distinct().ToArray();
        return (await connection.QueryAsync<ItemInfo>(new CommandDefinition("""
            SELECT i.id AS Id, i.item_type AS ItemType, i.tax_id AS TaxId,
                   i.price_includes_tax AS PriceIncludesTax,
                   COALESCE(i.default_price,
                            (SELECT iu.sale_price FROM item_units iu
                             WHERE iu.item_id = i.id AND iu.unit_id = i.base_unit_id), 0)
                       AS DefaultPrice
            FROM items i WHERE i.id = ANY(@ids) AND i.is_active
            """, new { ids }, tx, cancellationToken: ct))).ToDictionary(x => x.Id);
    }

    private static async Task<Dictionary<Guid, decimal>> LoadTaxRatesAsync(NpgsqlConnection c,
        NpgsqlTransaction tx, PosCheckoutInput input, Dictionary<Guid, ItemInfo> items,
        CancellationToken ct)
    {
        var ids = input.Lines.Select(l => l.TaxId ??
                (items.TryGetValue(l.ItemId, out var info) ? info.TaxId : null))
            .Where(id => id is not null).Select(id => id!.Value).Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<Guid, decimal>();
        return (await c.QueryAsync<(Guid Id, decimal Rate)>(new CommandDefinition(
            "SELECT id, rate FROM taxes WHERE id = ANY(@ids)", new { ids }, tx,
            cancellationToken: ct))).ToDictionary(x => x.Id, x => x.Rate);
    }

    private static List<BuiltLine> BuildLines(PosCheckoutInput input,
        Dictionary<Guid, decimal> rates, Dictionary<Guid, ItemInfo> items)
    {
        var result = new List<BuiltLine>();
        var lineNo = 0;
        foreach (var line in input.Lines)
        {
            lineNo++;
            if (line.Quantity <= 0)
                throw new ValidationAppException("validation.booking_quantity");
            if (!items.TryGetValue(line.ItemId, out var item))
                throw new ValidationAppException("validation.item_not_found");

            var price = line.UnitPrice > 0 ? line.UnitPrice : item.DefaultPrice;
            var gross = line.Quantity * price;
            var discount = Math.Round(gross * line.DiscountPercent / 100m, 2,
                MidpointRounding.AwayFromZero);
            var afterDiscount = Math.Round(gross - discount, 2, MidpointRounding.AwayFromZero);

            var taxId = line.TaxId ?? item.TaxId;
            var rate = taxId is not null && rates.TryGetValue(taxId.Value, out var value) ? value : 0m;

            decimal net, tax;
            if (item.PriceIncludesTax && rate > 0)
            {
                net = Math.Round(afterDiscount / (1 + rate / 100m), 2,
                    MidpointRounding.AwayFromZero);
                tax = Math.Round(afterDiscount - net, 2, MidpointRounding.AwayFromZero);
            }
            else
            {
                net = afterDiscount;
                tax = Math.Round(net * rate / 100m, 2, MidpointRounding.AwayFromZero);
            }

            result.Add(new BuiltLine
            {
                LineNo = lineNo, ItemId = line.ItemId, UnitId = line.UnitId,
                ItemType = item.ItemType, TaxId = taxId, Quantity = line.Quantity,
                UnitPrice = price, DiscountPercent = line.DiscountPercent, NetAmount = net,
                TaxAmount = tax,
                TotalAmount = Math.Round(net + tax, 2, MidpointRounding.AwayFromZero)
            });
        }
        return result;
    }

    /// <summary>
    /// استهلاك وصفات الخدمات عند البيع الفوري: تُصرف مكوّنات الوصفة من مخزن المستهلكات
    /// أو مخزن العملية، وتُثبت التكلفة بقيد Dr COGS / Cr Inventory.
    /// </summary>
    private static async Task ConsumeServiceRecipesAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, PosCheckoutInput input, IReadOnlyList<BuiltLine> lines,
        Guid userId, CancellationToken ct)
    {
        var services = lines.Where(l => l.ItemType == "SERVICE").ToList();
        if (services.Count == 0) return;

        var serviceIds = services.Select(s => s.ItemId).Distinct().ToArray();
        var components = (await connection.QueryAsync<(Guid ServiceItemId, Guid ComponentId,
            Guid UnitId, decimal Quantity)>(new CommandDefinition("""
            SELECT r.item_id AS ServiceItemId, ri.component_item_id AS ComponentId,
                   ri.unit_id AS UnitId, ri.quantity AS Quantity
            FROM service_recipe_items ri
            JOIN service_recipes r ON r.id = ri.recipe_id
            WHERE r.is_active AND r.item_id = ANY(@serviceIds) AND ri.consumption_mode = 0
            """, new { serviceIds }, tx, cancellationToken: ct))).ToList();
        if (components.Count == 0) return;

        var warehouseId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT consumables_warehouse_id FROM branch_settings WHERE branch_id = @branch",
            new { branch = input.BranchId }, tx, cancellationToken: ct)) ?? input.WarehouseId;

        var needs = components
            .Select(c => new
            {
                c.ComponentId, c.UnitId,
                Quantity = c.Quantity * services.Where(s => s.ItemId == c.ServiceItemId)
                    .Sum(s => s.Quantity)
            })
            .GroupBy(x => x.ComponentId)
            .Select(g => new
            {
                ComponentId = g.Key, UnitId = g.First().UnitId,
                Quantity = g.Sum(x => x.Quantity)
            })
            .Where(x => x.Quantity > 0)
            .ToList();

        var stockDocId = Guid.CreateVersion7();
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO stock_documents (id, doc_type, doc_date, branch_id, warehouse_id, status,
                notes, created_by)
            VALUES (@stockDocId, @docType, @docDate, @BranchId, @warehouseId, 'DRAFT', @notes, @userId)
            """, new { stockDocId, docType = StockDocumentTypes.Issue,
                docDate = input.DocDate.Date, input.BranchId, warehouseId,
                notes = "استهلاك وصفات POS", userId }, tx, cancellationToken: ct));

        var lineNo = 0;
        foreach (var need in needs)
        {
            lineNo++;
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO stock_document_lines (document_id, line_no, direction, item_id, unit_id,
                    conversion_factor, quantity, base_quantity, unit_cost)
                VALUES (@stockDocId, @lineNo, 'OUT', @itemId, @unitId, 1, @quantity, @quantity, 0)
                """, new { stockDocId, lineNo, itemId = need.ComponentId, unitId = need.UnitId,
                    quantity = need.Quantity }, tx, cancellationToken: ct));
        }

        await StockPostingRepository.PostCoreAsync(connection, tx, stockDocId, userId, false, ct);

        var cost = await connection.ExecuteScalarAsync<decimal>(new CommandDefinition(
            "SELECT COALESCE(SUM(total_cost), 0) FROM stock_document_lines WHERE document_id = @stockDocId",
            new { stockDocId }, tx, cancellationToken: ct));
        if (cost <= 0) return;

        await PostEntryAsync(connection, tx, "stock", input.BranchId, input.DocDate.Date,
            "POS-SERVICE", "تكلفة مكوّنات خدمات POS", new[]
            {
                new EntryLine(await NeedAsync(connection, tx, PostingKeys.StockCogs, ct), cost, 0),
                new EntryLine(await NeedAsync(connection, tx, PostingKeys.StockInventory, ct), 0, cost)
            }, userId, "stock_document", stockDocId, ct);
    }

    private static Task<string> NextNumberAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        string docType, Guid branchId, DateTime docDate, string prefix, CancellationToken ct) =>
        connection.ExecuteScalarAsync<string>(new CommandDefinition("""
            SELECT next_document_number(@docType::varchar, @branchId::uuid, @docDate::date,
                                        @prefix::varchar, 6::smallint)
            """, new { docType, branchId, docDate, prefix }, tx, cancellationToken: ct))!;

    private static async Task<Guid> NeedAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        string key, CancellationToken ct)
    {
        var account = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT account_id FROM posting_defaults WHERE setting_key = @key", new { key }, tx,
            cancellationToken: ct));
        return account ?? throw new ValidationAppException("validation.posting_account_missing",
            new Dictionary<string, string> { ["key"] = key, ["module"] = "sales" });
    }

    private static async Task<Guid> PostEntryAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, string journalType, Guid branchId, DateTime docDate, string docNo,
        string description, IReadOnlyList<EntryLine> gl, Guid userId, string sourceType,
        Guid? sourceId, CancellationToken ct)
    {
        var debit = gl.Sum(x => x.Debit);
        var credit = gl.Sum(x => x.Credit);
        if (Math.Round(debit - credit, 2, MidpointRounding.AwayFromZero) != 0)
            throw new ValidationAppException("validation.entry_not_balanced");

        var journalId = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "SELECT id FROM journals WHERE journal_type = @journalType AND is_active " +
            "ORDER BY sort_order LIMIT 1", new { journalType }, tx, cancellationToken: ct))
            ?? throw new ValidationAppException("validation.journal_not_found");

        var entryId = Guid.CreateVersion7();
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO journal_entries (id, journal_id, branch_id, entry_date, reference,
                description, status, source_type, source_id, total_debit, total_credit, created_by)
            VALUES (@entryId, @journalId, @branchId, @docDate, @docNo, @description, 'draft',
                    @sourceType, @sourceId, @debit, @credit, @userId)
            """, new { entryId, journalId, branchId, docDate, docNo, description, sourceType,
                sourceId, debit, credit, userId }, tx, cancellationToken: ct));

        var lineNo = 0;
        foreach (var line in gl)
        {
            lineNo++;
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO journal_entry_lines (id, entry_id, line_no, account_id, description,
                    debit, credit, customer_id, branch_id, entry_date)
                VALUES (@lineId, @entryId, @lineNo, @accountId, @description, @debit, @credit,
                        @customerId, @branchId, @docDate)
                """, new
            {
                lineId = Guid.CreateVersion7(), entryId, lineNo, accountId = line.AccountId,
                description, debit = line.Debit, credit = line.Credit,
                customerId = line.CustomerId, branchId, docDate
            }, tx, cancellationToken: ct));
        }

        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT fn_gl_post_entry(@entryId, @userId)", new { entryId, userId }, tx,
            cancellationToken: ct));
        return entryId;
    }
}
