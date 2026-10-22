using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;

namespace Ubp.Application.Services;

public sealed partial class AccountService
{
    private sealed record DefaultAccount(string Code, string? ParentCode, bool IsGroup,
        string Type, string NameAr, string NameEn, bool IsSystem = false);

    /// <summary>شجرة حسابات قياسية عربية/إنجليزية تُنشأ مرة واحدة وتُكمل الناقص فقط.</summary>
    private static readonly DefaultAccount[] DefaultTree =
    {
        new("1000", null, true, AccountTypes.AssetCurrent, "الأصول", "Assets"),
        new("1100", "1000", true, AccountTypes.AssetCurrent, "الأصول المتداولة", "Current Assets"),
        new("1101", "1100", false, AccountTypes.AssetCash, "النقدية بالصندوق", "Cash on Hand"),
        new("1102", "1100", false, AccountTypes.AssetCash, "النقدية بالبنوك", "Cash at Banks"),
        new("1110", "1100", false, AccountTypes.AssetReceivable, "العملاء", "Accounts Receivable", true),
        new("1120", "1100", false, AccountTypes.AssetCurrent, "المخزون", "Inventory", true),
        new("1130", "1100", false, AccountTypes.AssetCurrent, "مصروفات مدفوعة مقدماً", "Prepaid Expenses"),
        new("1200", "1000", true, AccountTypes.AssetFixed, "الأصول الثابتة", "Fixed Assets"),
        new("1210", "1200", false, AccountTypes.AssetFixed, "أثاث ومعدات", "Furniture & Equipment"),

        new("2000", null, true, AccountTypes.LiabilityCurrent, "الالتزامات", "Liabilities"),
        new("2100", "2000", false, AccountTypes.LiabilityPayable, "الموردون", "Accounts Payable", true),
        new("2200", "2000", false, AccountTypes.LiabilityCurrent, "مصروفات مستحقة", "Accrued Expenses"),

        new("3000", null, true, AccountTypes.Equity, "حقوق الملكية", "Equity"),
        new("3100", "3000", false, AccountTypes.Equity, "رأس المال", "Capital", true),
        new("3200", "3000", false, AccountTypes.Equity, "الأرباح المحتجزة", "Retained Earnings"),

        new("4000", null, true, AccountTypes.Income, "الإيرادات", "Revenue"),
        new("4100", "4000", false, AccountTypes.Income, "إيرادات المبيعات", "Sales Revenue"),
        new("4900", "4000", false, AccountTypes.Income, "إيرادات أخرى", "Other Revenue"),

        new("5000", null, true, AccountTypes.Expense, "المصروفات", "Expenses"),
        new("5100", "5000", false, AccountTypes.ExpenseCogs, "تكلفة المبيعات", "Cost of Goods Sold", true),
        new("5200", "5000", false, AccountTypes.Expense, "مصروفات إدارية", "Administrative Expenses"),
        new("5300", "5000", false, AccountTypes.Expense, "مصروفات تشغيلية", "Operating Expenses"),
        new("5410", "5000", false, AccountTypes.Income, "فروق جرد - ربح", "Inventory Gain", true),
        new("5420", "5000", false, AccountTypes.Expense, "فروق جرد - خسارة", "Inventory Loss", true),
        new("5490", "5000", false, AccountTypes.Expense, "فروق تقريب", "Rounding Differences", true)
    };

    public async Task<int> SeedDefaultTreeAsync(ActorContext actor, CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.Accounts, ActionCodes.LoadDefaultTree, ct: ct);
        var existing = (await _repository.ListAllAsync(true, ct))
            .ToDictionary(a => a.Code, StringComparer.OrdinalIgnoreCase);
        var created = 0;

        foreach (var node in DefaultTree)
        {
            if (existing.ContainsKey(node.Code)) continue;

            Guid? parentId = null;
            var parentPath = "/";
            short level = 1;
            if (node.ParentCode is not null && existing.TryGetValue(node.ParentCode, out var parent))
            {
                parentId = parent.Id;
                parentPath = parent.Path;
                level = (short)(parent.Level + 1);
            }

            var input = new AccountInput
            {
                Code = node.Code,
                NameAr = node.NameAr,
                NameEn = node.NameEn,
                ParentId = parentId,
                IsGroup = node.IsGroup,
                AccountType = node.Type,
                CostCenterPolicy = node.IsGroup ? CostCenterPolicies.NotAllowed : CostCenterPolicies.Optional,
                IsActive = true
            };

            try
            {
                var id = Guid.CreateVersion7();
                await _repository.CreateAsync(id, input, parentPath + node.Code + "/", level,
                    actor.UserId, ct);
                var account = await _repository.GetAsync(id, ct);
                if (account is null) continue;
                existing[node.Code] = account;
                created++;
            }
            catch (PostgresException ex)
            {
                throw DbErrorMapper.Map(ex);
            }
        }

        if (created > 0)
            await _audit.WriteAsync(actor, "accounts", "default-tree", "INSERT", null,
                new { created }, ct);
        return created;
    }
}
