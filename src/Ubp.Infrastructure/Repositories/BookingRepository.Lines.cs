using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class BookingRepository
{
    private sealed class BuiltLine
    {
        public int LineNo { get; set; }
        public Guid ItemId { get; set; }
        public Guid UnitId { get; set; }
        public Guid WarehouseId { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal DiscountPercent { get; set; }
        public Guid? TaxId { get; set; }
        public decimal NetAmount { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal TotalAmount { get; set; }
    }

    /// <summary>حساب البنود: خصم على السطر ثم ضريبة الصنف ثم الإجمالي (N5).</summary>
    private async Task<List<BuiltLine>> BuildLinesAsync(BookingInput input, CancellationToken ct)
    {
        if (input.BranchId == Guid.Empty || input.CustomerId == Guid.Empty)
            throw new ValidationAppException("validation.required_fields");
        if (input.Lines.Count == 0)
            throw new ValidationAppException("validation.document_has_no_lines");

        var taxIds = input.Lines.Where(l => l.TaxId.HasValue).Select(l => l.TaxId!.Value)
            .Distinct().ToArray();
        var rates = new Dictionary<Guid, decimal>();
        if (taxIds.Length > 0)
        {
            await using var connection = await _factory.OpenAsync(ct);
            rates = (await connection.QueryAsync<(Guid Id, decimal Rate)>(new CommandDefinition(
                "SELECT id, rate FROM taxes WHERE id = ANY(@taxIds)", new { taxIds },
                cancellationToken: ct))).ToDictionary(x => x.Id, x => x.Rate);
        }

        var result = new List<BuiltLine>();
        var lineNo = 0;
        foreach (var line in input.Lines)
        {
            lineNo++;
            if (line.Quantity <= 0)
                throw new ValidationAppException("validation.booking_quantity");
            if (line.ItemId == Guid.Empty || line.UnitId == Guid.Empty ||
                line.WarehouseId == Guid.Empty)
                throw new ValidationAppException("validation.required_fields");

            var gross = line.Quantity * line.UnitPrice;
            var discount = Math.Round(gross * line.DiscountPercent / 100m, 2,
                MidpointRounding.AwayFromZero);
            var net = Math.Round(gross - discount, 2, MidpointRounding.AwayFromZero);
            var rate = line.TaxId is not null && rates.TryGetValue(line.TaxId.Value, out var value)
                ? value : 0m;
            var tax = Math.Round(net * rate / 100m, 2, MidpointRounding.AwayFromZero);

            result.Add(new BuiltLine
            {
                LineNo = lineNo, ItemId = line.ItemId, UnitId = line.UnitId,
                WarehouseId = line.WarehouseId, Quantity = line.Quantity,
                UnitPrice = line.UnitPrice, DiscountPercent = line.DiscountPercent,
                TaxId = line.TaxId, NetAmount = net, TaxAmount = tax,
                TotalAmount = Math.Round(net + tax, 2, MidpointRounding.AwayFromZero)
            });
        }

        return result;
    }

    private static async Task InsertLinesAsync(NpgsqlConnection connection, NpgsqlTransaction tx,
        Guid bookingId, IReadOnlyList<BuiltLine> lines, CancellationToken ct)
    {
        foreach (var line in lines)
        {
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO booking_lines (booking_id, line_no, item_id, unit_id, warehouse_id,
                    quantity, unit_price, discount_percent, tax_id, net_amount, tax_amount,
                    total_amount)
                VALUES (@bookingId, @LineNo, @ItemId, @UnitId, @WarehouseId, @Quantity,
                        @UnitPrice, @DiscountPercent, @TaxId, @NetAmount, @TaxAmount, @TotalAmount)
                """, new
            {
                bookingId, line.LineNo, line.ItemId, line.UnitId, line.WarehouseId, line.Quantity,
                line.UnitPrice, line.DiscountPercent, line.TaxId, line.NetAmount, line.TaxAmount,
                line.TotalAmount
            }, tx, cancellationToken: ct));
        }
    }
}
