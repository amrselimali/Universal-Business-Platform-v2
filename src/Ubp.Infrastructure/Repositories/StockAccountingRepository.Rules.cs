using Ubp.Core.Models;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class StockAccountingRepository
{
    private static void Accumulate(StockLineRow line, decimal amount,
        IReadOnlyDictionary<string, Guid> defaults,
        Dictionary<Guid, (decimal Debit, decimal Credit)> amounts, BuildResult result)
    {
        var inventory = line.InventoryAccountId;
        if (inventory is null)
        {
            result.MissingCategoryAccounts = true;
            return;
        }

        if (line.EffectiveType == StockDocumentTypes.Opening)
        {
            var equity = Counterparty(defaults, PostingKeys.OpeningEquity)
                         ?? Counterparty(defaults, PostingKeys.StockInventory);
            if (equity is null) { result.MissingCategoryAccounts = true; return; }
            if (line.Direction == "IN") { Add(amounts, inventory.Value, amount, 0); Add(amounts, equity.Value, 0, amount); }
            else { Add(amounts, equity.Value, amount, 0); Add(amounts, inventory.Value, 0, amount); }
            return;
        }

        if (line.EffectiveType == StockDocumentTypes.Adjustment)
        {
            var gain = line.AdjustmentGainAccountId
                       ?? Counterparty(defaults, PostingKeys.StockAdjustmentGain);
            var loss = line.AdjustmentLossAccountId
                       ?? Counterparty(defaults, PostingKeys.StockAdjustmentLoss);
            if (line.Direction == "IN")
            {
                if (gain is null) { result.MissingCategoryAccounts = true; return; }
                Add(amounts, inventory.Value, amount, 0); Add(amounts, gain.Value, 0, amount);
            }
            else
            {
                if (loss is null) { result.MissingCategoryAccounts = true; return; }
                Add(amounts, loss.Value, amount, 0); Add(amounts, inventory.Value, 0, amount);
            }
            return;
        }

        if (line.EffectiveType == StockDocumentTypes.Issue)
        {
            var cogs = line.CogsAccountId ?? Counterparty(defaults, PostingKeys.StockCogs);
            if (cogs is null) { result.MissingCategoryAccounts = true; return; }
            if (line.Direction == "OUT") { Add(amounts, cogs.Value, amount, 0); Add(amounts, inventory.Value, 0, amount); }
            else { Add(amounts, inventory.Value, amount, 0); Add(amounts, cogs.Value, 0, amount); }
            return;
        }

        var clearing = Counterparty(defaults, PostingKeys.StockInventory);
        if (clearing is null) { result.MissingCategoryAccounts = true; return; }
        if (line.Direction == "IN") { Add(amounts, inventory.Value, amount, 0); Add(amounts, clearing.Value, 0, amount); }
        else { Add(amounts, clearing.Value, amount, 0); Add(amounts, inventory.Value, 0, amount); }
    }

    private static void Add(Dictionary<Guid, (decimal Debit, decimal Credit)> amounts,
        Guid accountId, decimal debit, decimal credit)
    {
        amounts.TryGetValue(accountId, out var current);
        amounts[accountId] = (current.Debit + debit, current.Credit + credit);
    }

    private static Guid? Counterparty(IReadOnlyDictionary<string, Guid> defaults, string key) =>
        defaults.TryGetValue(key, out var id) ? id : null;

    /// <summary>فرق التقريب يُضاف لحساب التقريب إن وُجد، وإلا يُسوّى على أكبر بند.</summary>
    private static void ApplyRounding(BuildResult result, IReadOnlyDictionary<string, Guid> defaults,
        decimal difference)
    {
        if (defaults.TryGetValue(PostingKeys.Rounding, out var roundingAccount))
        {
            result.Lines.Add(new JournalEntryLineInput
            {
                AccountId = roundingAccount,
                Debit = difference < 0 ? -difference : 0,
                Credit = difference > 0 ? difference : 0
            });
            return;
        }

        var target = difference > 0
            ? result.Lines.OrderByDescending(l => l.Debit).First()
            : result.Lines.OrderByDescending(l => l.Credit).First();
        if (difference > 0) target.Debit -= difference;
        else target.Credit += difference;
    }

    private sealed class StockLineRow
    {
        public string Direction { get; set; } = "IN";
        public decimal TotalCost { get; set; }
        public string EffectiveType { get; set; } = "";
        public Guid? CategoryId { get; set; }
        public Guid? InventoryAccountId { get; set; }
        public Guid? CogsAccountId { get; set; }
        public Guid? AdjustmentGainAccountId { get; set; }
        public Guid? AdjustmentLossAccountId { get; set; }
    }

    private sealed class BuildResult
    {
        public List<JournalEntryLineInput> Lines { get; set; } = new();
        public string? ErrorKey { get; set; }
        public bool MissingCategoryAccounts { get; set; }
    }

    private sealed class DocHead
    {
        public Guid Id { get; set; }
        public string? DocNo { get; set; }
        public string DocType { get; set; } = "";
        public DateTime DocDate { get; set; }
        public Guid BranchId { get; set; }
        public string? BranchName { get; set; }
        public decimal TotalCost { get; set; }
        public string Status { get; set; } = "";
    }
}
