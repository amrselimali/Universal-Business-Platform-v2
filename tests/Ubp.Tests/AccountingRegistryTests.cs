using System.Text.RegularExpressions;
using Ubp.Core.Models;
using Ubp.Core.Security;
using Xunit;

namespace Ubp.Tests;

public sealed class AccountingRegistryTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public AccountingRegistryTests(TestDb db) => _db = db;

    [Fact]
    public async Task Every_permission_checked_in_accounting_screens_exists_in_registry()
    {
        var webRoot = FindWebRoot();
        if (webRoot is null) return;

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Accounts.razor"] = ScreenCodes.Accounts,
            ["CostCenters.razor"] = ScreenCodes.CostCenters,
            ["Journals.razor"] = ScreenCodes.Journals,
            ["AccountingSettingsPage.razor"] = ScreenCodes.AccountingSettings,
            ["Entries.razor"] = ScreenCodes.JournalEntries,
            ["EntryEditor.razor"] = ScreenCodes.JournalEntry,
            ["OpeningBalances.razor"] = ScreenCodes.OpeningBalances,
            ["StockPostingCenter.razor"] = ScreenCodes.StockPostingCenter,
            ["Ledger.razor"] = ScreenCodes.LedgerReport,
            ["TrialBalance.razor"] = ScreenCodes.TrialBalanceReport,
            ["PartyStatement.razor"] = ScreenCodes.PartyStatementReport,
            ["CostCenterReport.razor"] = ScreenCodes.CostCenterReport,
            ["StockVsGl.razor"] = ScreenCodes.StockVsGlReport,
            ["Employees.razor"] = ScreenCodes.Employees,
            ["Taxes.razor"] = ScreenCodes.Taxes,
            ["PaymentTerms.razor"] = ScreenCodes.PaymentTerms,
            ["PriceLists.razor"] = ScreenCodes.PriceLists,
            ["Invoices.razor"] = ScreenCodes.PurchaseInvoices,
            ["Management.razor"] = ScreenCodes.Treasuries,
            ["Vouchers.razor"] = ScreenCodes.TreasuryVouchers,
            ["Shifts.razor"] = ScreenCodes.TreasuryShifts,
            ["Reconciliation.razor"] = ScreenCodes.TreasuryReconciliation,
            ["Bookings.razor"] = ScreenCodes.SalesBookings,
            ["Appointments.razor"] = ScreenCodes.SalesAppointments,
            ["Deliveries.razor"] = ScreenCodes.SalesDeliveries,
            ["SalesInvoices.razor"] = ScreenCodes.SalesInvoices,
            ["SalesReturns.razor"] = ScreenCodes.SalesReturns,
            ["Settlements.razor"] = ScreenCodes.SalesSettlements,
            ["Commissions.razor"] = ScreenCodes.SalesCommissions,
            ["Aging.razor"] = ScreenCodes.AgingReport,
            ["PostingCenter.razor"] = ScreenCodes.PostingCenter,
            ["Terminal.razor"] = ScreenCodes.PosTerminal,
            ["Receipts.razor"] = ScreenCodes.PosReceipts,
            ["FollowUps.razor"] = ScreenCodes.FollowUps
        };

        var pattern = new Regex(@"Can\(ActionCodes\.(\w+)\)", RegexOptions.Compiled);
        var actionCodes = typeof(ActionCodes)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue()!,
                StringComparer.OrdinalIgnoreCase);
        var checkedFiles = 0;

        foreach (var (fileName, screenCode) in map)
        {
            var files = Directory.GetFiles(webRoot, fileName, SearchOption.AllDirectories);
            if (files.Length == 0) continue;
            checkedFiles++;

            var actions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                foreach (var line in File.ReadLines(file))
                {
                    if (line.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                    foreach (Match match in pattern.Matches(line))
                        actions.Add(match.Groups[1].Value);
                }
            }

            foreach (var action in actions)
            {
                var code = actionCodes.TryGetValue(action, out var value) ? value : action;
                Assert.True(AppRegistry.IsRegistered(screenCode, code),
                    $"الزر {action} في {fileName} غير مسجل في AppRegistry للشاشة {screenCode}");
            }
        }

        Assert.True(checkedFiles >= 10, "لم يتم العثور على ملفات الشاشات المحاسبية");
    }

    [Fact]
    public async Task Completeness_detects_missing_start_date_and_passes_after_setup()
    {
        var actor = await _db.AdminActorAsync();
        await _db.SeedAccountingAsync();
        var settings = _db.AccountingSettings();

        var before = await settings.CheckCompletenessAsync(actor);
        Assert.False(before.IsComplete);
        Assert.Contains(before.Items, i => i.Code == "start_date" && !i.Ok && i.Blocking);

        var current = await settings.GetAsync(actor);
        current.AccountingStartDate = new DateTime(2026, 1, 1);
        await settings.SaveAsync(actor, current);

        var after = await settings.CheckCompletenessAsync(actor);
        Assert.True(after.IsComplete, string.Join(", ",
            after.Items.Where(i => !i.Ok && i.Blocking).Select(i => i.Code)));
    }

    [Fact]
    public async Task Ledger_keyset_pages_are_disjoint_and_keep_running_balance()
    {
        var actor = await _db.AdminActorAsync();
        var chart = await _db.SeedAccountingAsync();
        var branch = await _db.CreateBranchAsync("GL-L1", "فرع الأستاذ");

        for (var i = 0; i < 12; i++)
        {
            await _db.PostBalancedEntryAsync(branch, new DateTime(2026, 5, 1).AddDays(i),
                chart["1101"].Id, chart["4100"].Id, 10 + i);
        }

        var request = new LedgerRequest
        {
            AccountId = chart["1101"].Id, BranchId = branch,
            From = new DateTime(2026, 5, 1), To = new DateTime(2026, 5, 31),
            IncludeChildren = false, PageSize = 5
        };

        var first = await _db.AccountingReports().LedgerAsync(actor, request);
        Assert.Equal(5, first.Rows.Count);
        Assert.True(first.HasMore);

        request.Cursor = LedgerCursor.From(first.NextCursor);
        var second = await _db.AccountingReports().LedgerAsync(actor, request);
        Assert.Equal(5, second.Rows.Count);

        Assert.Empty(first.Rows.Select(r => r.LineId).Intersect(second.Rows.Select(r => r.LineId)));
        Assert.Equal(first.Rows[^1].RunningBalance + second.Rows[0].Debit - second.Rows[0].Credit,
            second.Rows[0].RunningBalance);

        var june = await _db.AccountingReports().LedgerAsync(actor, new LedgerRequest
        {
            AccountId = chart["1101"].Id, BranchId = branch,
            From = new DateTime(2026, 6, 1), To = new DateTime(2026, 6, 30),
            IncludeChildren = false
        });
        Assert.Equal(12 * 10 + 66, june.OpeningBalance);
        Assert.Empty(june.Rows);
    }

    private static string? FindWebRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Ubp.Web", "Pages", "Accounting");
            if (Directory.Exists(candidate))
                return Path.Combine(directory.FullName, "src", "Ubp.Web");
            directory = directory.Parent;
        }
        return null;
    }
}
