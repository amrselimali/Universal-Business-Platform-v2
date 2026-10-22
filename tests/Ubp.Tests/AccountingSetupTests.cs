using System.Text.RegularExpressions;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Xunit;

namespace Ubp.Tests;

public sealed class AccountingSetupTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public AccountingSetupTests(TestDb db) => _db = db;

    [Fact]
    public async Task Default_tree_is_idempotent_and_builds_paths_and_levels()
    {
        var actor = await _db.AdminActorAsync();
        var first = await _db.SeedAccountingAsync();
        await _db.AccountingSettings().LoadDefaultTreeAsync(actor);
        var second = await _db.SeedAccountingAsync();

        Assert.Equal(first.Count, second.Count);
        Assert.Equal("/1000/1100/1101/", first["1101"].Path);
        Assert.Equal(3, first["1101"].Level);
        Assert.True(first["1110"].IsSystem);
        Assert.True(first["1100"].IsGroup);
        Assert.False(first["1120"].IsGroup);
    }

    [Fact]
    public async Task Move_updates_subtree_paths_and_levels()
    {
        var actor = await _db.AdminActorAsync();
        var accounts = _db.AccountService();

        var root = await accounts.CreateRootAsync(actor, new AccountInput
        {
            Code = "9000", NameAr = "حسابات اختبار", IsGroup = true,
            AccountType = AccountTypes.Expense
        });
        var child = await accounts.CreateChildAsync(actor, root.Id, new AccountInput
        {
            Code = "9100", NameAr = "فرع اختبار", IsGroup = true, AccountType = AccountTypes.Expense
        });
        var grandChild = await accounts.CreateChildAsync(actor, child.Id, new AccountInput
        {
            Code = "9101", NameAr = "فرع فرعي", AccountType = AccountTypes.Expense
        });

        Assert.Equal("/9000/9100/9101/", grandChild.Path);
        Assert.Equal(3, grandChild.Level);

        var moved = await accounts.MoveAsync(actor, child.Id, null);

        Assert.Equal("/9100/", moved.Path);
        Assert.Equal(1, moved.Level);
        var reloaded = await _db.AccountRepo.GetAsync(grandChild.Id);
        Assert.Equal("/9100/9101/", reloaded!.Path);
        Assert.Equal(2, reloaded.Level);
    }

    [Fact]
    public async Task Move_into_own_descendant_is_rejected()
    {
        var actor = await _db.AdminActorAsync();
        var accounts = _db.AccountService();

        var root = await accounts.CreateRootAsync(actor, new AccountInput
        {
            Code = "9200", NameAr = "جذر", IsGroup = true, AccountType = AccountTypes.Expense
        });
        var child = await accounts.CreateChildAsync(actor, root.Id, new AccountInput
        {
            Code = "9210", NameAr = "ابن", IsGroup = true, AccountType = AccountTypes.Expense
        });

        var exception = await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await accounts.MoveAsync(actor, root.Id, child.Id));
        Assert.Equal("validation.move_blocked", exception.Code);
    }

    [Fact]
    public void Accounting_screens_are_registered_with_accounting_permission_keys()
    {
        var accountingScreens = new[]
        {
            ScreenCodes.Accounts, ScreenCodes.CostCenters, ScreenCodes.Journals,
            ScreenCodes.AccountingSettings, ScreenCodes.JournalEntries, ScreenCodes.JournalEntry,
            ScreenCodes.OpeningBalances, ScreenCodes.StockPostingCenter,
            ScreenCodes.LedgerReport, ScreenCodes.TrialBalanceReport,
            ScreenCodes.PartyStatementReport, ScreenCodes.CostCenterReport, ScreenCodes.StockVsGlReport
        };

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var code in accountingScreens)
        {
            Assert.True(AppRegistry.ByCode.ContainsKey(code), "الشاشة غير مسجلة: " + code);
            var screen = AppRegistry.ByCode[code];
            Assert.False(string.IsNullOrWhiteSpace(screen.PermissionGroup));
            Assert.NotEmpty(screen.Actions);

            foreach (var action in screen.Actions)
            {
                var key = screen.PermissionKey(action.Code);
                Assert.StartsWith("accounting.", key);
                Assert.True(keys.Add(key), "مفتاح مكرر: " + key);
            }
        }

        Assert.Equal("accounting.accounts.create_root",
            AppRegistry.ByCode[ScreenCodes.Accounts].PermissionKey(ActionCodes.CreateRoot));
    }
}
