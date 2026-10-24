using Dapper;
using Npgsql;
using Ubp.Core.Models;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class CommercialDocumentRepository
{
    private sealed record LineAmounts(decimal DiscountAmount, decimal NetAmount, decimal TaxRate,
        decimal TaxAmount, decimal TotalAmount);

    private sealed record DocTotals(decimal Subtotal, decimal DiscountTotal, decimal TaxTotal,
        decimal Total, IReadOnlyList<LineAmounts> Lines);

    /// <summary>حساب المبالغ لكل بند (N5): خصم ثم صافي ثم ضريبة لكل بند وتجميعها في الرأس.</summary>
    private static async Task<DocTotals> ComputeTotalsAsync(NpgsqlConnection connection,
        NpgsqlTransaction tx, IReadOnlyList<CommercialDocumentLineInput> lines, CancellationToken ct)
    {
        var rates = new Dictionary<Guid, decimal>();
        var taxIds = lines.Where(l => l.TaxId.HasValue).Select(l => l.TaxId!.Value).Distinct().ToList();
        if (taxIds.Count > 0)
        {
            var rows = await connection.QueryAsync<(Guid Id, decimal Rate)>(new CommandDefinition(
                "SELECT id, rate FROM taxes WHERE id = ANY(@taxIds)", new { taxIds }, tx,
                cancellationToken: ct));
            rates = rows.ToDictionary(r => r.Id, r => r.Rate);
        }

        var computed = new List<LineAmounts>(lines.Count);
        decimal gross = 0, discount = 0, tax = 0, total = 0;

        foreach (var line in lines)
        {
            var lineGross = Math.Round(line.Quantity * line.UnitPrice, 2, MidpointRounding.AwayFromZero);
            var lineDiscount = Math.Round(lineGross * line.DiscountPercent / 100m, 2,
                MidpointRounding.AwayFromZero);
            var net = lineGross - lineDiscount;
            var rate = line.TaxId.HasValue && rates.TryGetValue(line.TaxId.Value, out var r) ? r : 0m;
            var lineTax = Math.Round(net * rate / 100m, 2, MidpointRounding.AwayFromZero);

            computed.Add(new LineAmounts(lineDiscount, net, rate, lineTax, net + lineTax));
            gross += lineGross;
            discount += lineDiscount;
            tax += lineTax;
            total += net + lineTax;
        }

        return new DocTotals(gross, discount, tax, total, computed);
    }
}
