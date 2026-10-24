using System.Globalization;
using Ubp.Core.Models;
using Ubp.Core.Security;

namespace Ubp.Application.Services;

public sealed partial class OpeningBalanceService
{
    /// <summary>
    /// استيراد CSV: code,debit,credit,description,party_code,cost_center_code
    /// سطر العنوان اختياري، ويُقبل الفاصل "," أو ";" — تُرجع أخطاء السطر برقمه.
    /// </summary>
    public async Task<OpeningBalanceImportResult> ImportCsvAsync(ActorContext actor, string csv,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.OpeningBalances, ActionCodes.ImportCsv,
            ct: ct);

        var result = new OpeningBalanceImportResult();
        if (string.IsNullOrWhiteSpace(csv)) return result;

        var rows = csv.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var accounts = (await _accounts.ListPostableAsync(ct)).ToDictionary(a => a.Code,
            StringComparer.OrdinalIgnoreCase);
        var first = true;

        for (var i = 0; i < rows.Length; i++)
        {
            var raw = rows[i].Trim();
            if (raw.Length == 0) continue;
            var rowNumber = i + 1;
            var parts = (raw.Contains(';') ? raw.Split(';') : raw.Split(','))

                .Select(p => p.Trim().Trim('"')).ToArray();

            if (first)
            {
                first = false;
                if (parts[0].TrimStart('\uFEFF').Equals("code", StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            if (parts.Length < 3)
            {
                result.Errors.Add(new OpeningBalanceError
                {
                    Row = rowNumber, Key = "validation.csv_columns"
                });
                continue;
            }

            if (!accounts.TryGetValue(parts[0], out var account))
            {
                result.Errors.Add(new OpeningBalanceError
                {
                    Row = rowNumber, Key = "validation.account_not_found", Detail = parts[0]
                });
                continue;
            }

            if (!TryAmount(parts[1], out var debit) || !TryAmount(parts[2], out var credit) ||
                debit < 0 || credit < 0 || (debit > 0 && credit > 0) || (debit == 0 && credit == 0))
            {
                result.Errors.Add(new OpeningBalanceError
                {
                    Row = rowNumber, Key = "validation.line_amount"
                });
                continue;
            }

            var line = new JournalEntryLineInput
            {
                AccountId = account.Id,
                Debit = debit,
                Credit = credit,
                Description = parts.Length > 3 && parts[3].Length > 0 ? parts[3] : null
            };

            var partyCode = parts.Length > 4 ? parts[4] : "";
            if (!string.IsNullOrEmpty(partyCode))
            {
                var party = account.RequiresCustomer
                    ? await _partners.GetByCodeAsync(PartnerKind.Customer, partyCode, ct)
                    : account.RequiresSupplier
                        ? await _partners.GetByCodeAsync(PartnerKind.Supplier, partyCode, ct)
                        : null;

                if (party is null)
                {
                    result.Errors.Add(new OpeningBalanceError
                    {
                        Row = rowNumber, Key = "validation.party_not_found", Detail = partyCode
                    });
                    continue;
                }

                if (account.RequiresCustomer) line.CustomerId = party.Id;
                else line.SupplierId = party.Id;
            }
            else if (account.RequiresParty)
            {
                result.Errors.Add(new OpeningBalanceError
                {
                    Row = rowNumber, Key = "validation.party_required"
                });
                continue;
            }

            var costCenterCode = parts.Length > 5 ? parts[5] : "";
            if (!string.IsNullOrEmpty(costCenterCode))
            {
                var costCenter = await _costCenters.GetByCodeAsync(costCenterCode, ct);
                if (costCenter is null)
                {
                    result.Errors.Add(new OpeningBalanceError
                    {
                        Row = rowNumber, Key = "validation.cost_center_not_found", Detail = costCenterCode
                    });
                    continue;
                }
                line.CostCenterId = costCenter.Id;
            }

            result.Lines.Add(line);
        }

        return result;
    }

    private static bool TryAmount(string raw, out decimal value) =>
        decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out value) ||
        decimal.TryParse(raw, NumberStyles.Number, CultureInfo.CurrentCulture, out value);
}
