using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Xunit;

namespace Ubp.Tests;

public sealed class Phase2WorkflowTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public Phase2WorkflowTests(TestDb db) => _db = db;

    [Fact]
    public void Phase2_screens_are_registered_with_view_actions()
    {
        var expected = new[]
        {
            ScreenCodes.Units, ScreenCodes.ItemCategories, ScreenCodes.Items,
            ScreenCodes.Customers, ScreenCodes.Suppliers, ScreenCodes.StockDocuments,
            ScreenCodes.Stocktake, ScreenCodes.FinancialPeriods, ScreenCodes.StockReports
        };

        foreach (var screen in expected)
        {
            Assert.True(AppRegistry.ByCode.ContainsKey(screen), $"missing screen {screen}");
            Assert.True(AppRegistry.IsRegistered(screen, ActionCodes.View), $"missing VIEW {screen}");
        }

        Assert.Equal(AppRegistry.Screens.Count,
            AppRegistry.Screens.Select(s => s.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Phase2_screens_are_default_denied_for_non_admins()
    {
        var result = PermissionEvaluator.Evaluate(Array.Empty<PermissionEntry>(),
            Array.Empty<PermissionEntry>(), false, ScreenCodes.StockDocuments, ActionCodes.Post);
        Assert.False(result.Allowed);
        Assert.Equal("none", result.Source);
    }

    [Fact]
    public async Task Closing_a_branch_is_blocked_by_drafts_then_blocks_posting()
    {
        var branch = await _db.CreateBranchAsync("CLS-B", "فرع الإغلاق");
        var warehouse = await _db.CreateWarehouseAsync("CLS-W", "مخزن الإغلاق", branch);
        var item = await _db.CreateItemAsync("IT-CLS", "صنف الإغلاق");
        var actor = await _db.AdminActorAsync();

        var period = await _db.PeriodService().CreateAsync(actor, new FiscalPeriodInput
        {
            Code = "CLS-" + Guid.NewGuid().ToString("N")[..6],
            NameAr = "فترة الإغلاق",
            StartDate = DateTime.UtcNow.Date.AddDays(-1),
            EndDate = DateTime.UtcNow.Date.AddDays(1)
        });

        var draft = await _db.StockDocRepo.CreateDraftAsync(new StockDocumentInput
        {
            DocType = StockDocumentTypes.Receipt,
            DocDate = DateTime.UtcNow.Date,
            BranchId = branch,
            WarehouseId = warehouse,
            Lines =
            {
                new StockDocumentLineInput
                {
                    ItemId = item.Id, UnitId = item.BaseUnitId, ConversionFactor = 1,
                    Quantity = 1, UnitCost = 1
                }
            }
        }, actor.UserId);

        var blocked = await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await _db.PeriodService().CloseAsync(actor, period.Id, PeriodScopes.Branch,
                branch, PeriodModules.Inventory));
        Assert.Equal("validation.period_close_blocked", blocked.Code);

        await _db.StockDocRepo.DeleteDraftAsync(draft.Id);
        await _db.PeriodService().CloseAsync(actor, period.Id, PeriodScopes.Branch,
            branch, PeriodModules.Inventory);

        var closed = await _db.PeriodRepo.GetClosureAsync(period.Id, PeriodScopes.Branch,
            branch, PeriodModules.Inventory);
        Assert.NotNull(closed);
        Assert.Equal("CLOSED", closed!.Status);

        var posting = await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await _db.PostReceiptAsync(item, branch, warehouse, 1, 2));
        Assert.Equal("validation.period_closed", posting.Code);

        await _db.PeriodService().ReopenAsync(actor, period.Id, PeriodScopes.Branch, branch,
            PeriodModules.Inventory, "مراجعة تسوية");
        await _db.PostReceiptAsync(item, branch, warehouse, 1, 2);
        Assert.Equal(1, await _db.OnHandAsync(item.Id, warehouse));
    }
}
