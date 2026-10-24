using Dapper;
using Npgsql;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Core.Results;
using Xunit;

namespace Ubp.Tests;

public sealed class Phase4SetupTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public Phase4SetupTests(TestDb db) => _db = db;

    [Fact]
    public async Task Posting_default_types_are_seeded_and_cover_all_used_keys()
    {
        var types = await _db.AccountingSettingsRepo.ListTypesAsync();

        Assert.True(types.Count >= 13);
        Assert.Contains(types, t => t.Key == "sales_revenue" && t.Module == "sales" && t.IsRequired);
        Assert.Contains(types, t => t.Key == "customer_receivable" && t.PartyType == "C");
        Assert.Contains(types, t => t.Key == "supplier_payable" && t.PartyType == "S");
        Assert.Contains(types, t => t.Key == "stock_inventory" && t.Module == "inventory");

        var known = types.Select(t => t.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var key in PostingKeys.All)
            Assert.Contains(key, known);
    }

    [Fact]
    public async Task Readiness_blocks_only_the_requested_module_and_names_the_key()
    {
        var actor = await _db.AdminActorAsync();
        var chart = await _db.SeedAccountingAsync();
        var settings = _db.AccountingSettings();
        var readiness = _db.Readiness();

        var sales = await readiness.MissingAsync("sales");
        Assert.Contains("sales_revenue", sales);
        Assert.Empty(await readiness.MissingAsync("inventory"));

        var exception = await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await readiness.RequireAsync("sales"));
        Assert.Equal("validation.posting_account_missing", exception.Code);
        Assert.False(string.IsNullOrEmpty(exception.Args["key"]));

        await settings.SetDefaultAsync(actor, "sales_revenue", chart["4100"].Id);
        await settings.SetDefaultAsync(actor, "customer_receivable", chart["1110"].Id);
        PostingReadinessService.InvalidateAll();

        Assert.Empty(await _db.Readiness().MissingAsync("sales"));
    }

    [Fact]
    public async Task Employee_requires_at_least_one_role_and_unique_code()
    {
        var actor = await _db.AdminActorAsync();
        var service = _db.MasterData();

        var invalid = await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await service.SaveEmployeeAsync(actor, null, 0, new EmployeeInput
            { Code = "E1", NameAr = "موظف بلا دور" }));
        Assert.Equal("validation.employee_role_required", invalid.Code);

        var created = await service.SaveEmployeeAsync(actor, null, 0, new EmployeeInput
        { Code = "E1", NameAr = "موظف مبيعات", IsSeller = true });

        Assert.True(created.IsSeller);
        var duplicate = await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await service.SaveEmployeeAsync(actor, null, 0, new EmployeeInput
            { Code = "E1", NameAr = "مكرر", IsSeller = true }));
        Assert.Equal("validation.duplicate_code", duplicate.Code);
    }

    [Fact]
    public async Task Tax_and_payment_term_validation()
    {
        var actor = await _db.AdminActorAsync();
        var service = _db.MasterData();

        var badTax = await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await service.SaveTaxAsync(actor, null, 0, new TaxInput
            { Code = "VAT", NameAr = "ضريبة", Rate = 120 }));
        Assert.Equal("validation.tax_rate", badTax.Code);

        var tax = await service.SaveTaxAsync(actor, null, 0, new TaxInput
        { Code = "VAT14", NameAr = "ضريبة 14%", Rate = 14m });
        Assert.Equal(14m, tax.Rate);

        var term = await service.SavePaymentTermAsync(actor, null, 0, new PaymentTermInput
        { Code = "NET30", NameAr = "صافي 30", NetDays = 30 });
        Assert.Equal(30, term.NetDays);
        Assert.Single(await service.TaxOptionsAsync(actor));
    }

    [Fact]
    public async Task Price_list_items_replace_and_resolve_by_quantity()
    {
        var actor = await _db.AdminActorAsync();
        var service = _db.MasterData();
        var item = await _db.CreateItemAsync("IT-PL", "صنف قائمة أسعار");

        var list = await service.SavePriceListAsync(actor, null, 0, new PriceListInput
        { Code = "RETAIL", NameAr = "تجزئة", IsDefault = true });

        await service.ReplacePriceListItemsAsync(actor, list.Id, new List<PriceListItemInput>
        {
            new() { ItemId = item.Id, Price = 100m, MinQty = 0m },
            new() { ItemId = item.Id, Price = 90m, MinQty = 10m }
        });

        var rows = await service.PriceListItemsAsync(actor, list.Id);
        Assert.Equal(2, rows.Count);
        Assert.Equal(90m, await _db.PriceListRepo.ResolvePriceAsync(list.Id, item.Id, 12));
        Assert.Equal(100m, await _db.PriceListRepo.ResolvePriceAsync(list.Id, item.Id, 5));
    }

    [Fact]
    public async Task Recipe_saves_lines_and_rejects_self_reference()
    {
        var actor = await _db.AdminActorAsync();
        var unit = await _db.UnitRepo.CreateAsync(new UnitInput { Code = "U-RCP", NameAr = "وحدة" });
        var serviceItem = await _db.ItemRepo.CreateAsync(new ItemInput
        {
            Code = "SRV-1", NameAr = "خدمة", ItemType = "SERVICE", BaseUnitId = unit.Id,
            Units = { new ItemUnitInput { UnitId = unit.Id, ConversionFactor = 1 } }
        });
        var component = await _db.ItemRepo.CreateAsync(new ItemInput
        {
            Code = "CMP-1", NameAr = "مستهلك", BaseUnitId = unit.Id,
            Units = { new ItemUnitInput { UnitId = unit.Id, ConversionFactor = 1 } }
        });

        var recipe = await _db.RecipeService().SaveAsync(actor, null, new ServiceRecipeInput
        {
            ItemId = serviceItem.Id,
            Items =
            {
                new ServiceRecipeItemInput
                {
                    ComponentItemId = component.Id, UnitId = unit.Id, Quantity = 2,
                    ConsumptionMode = ConsumptionModes.Editable
                }
            }
        });

        Assert.Single(recipe.Items);
        Assert.Equal(2m, recipe.Items[0].Quantity);

        var self = await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await _db.RecipeService().SaveAsync(actor, null, new ServiceRecipeInput
            {
                ItemId = serviceItem.Id,
                Items =
                {
                    new ServiceRecipeItemInput
                    { ComponentItemId = serviceItem.Id, UnitId = unit.Id, Quantity = 1 }
                }
            }));
        Assert.Equal("validation.recipe_self_reference", self.Code);
    }

    [Fact]
    public async Task Branch_settings_accepts_only_a_consumables_warehouse_of_the_same_branch()
    {
        var actor = await _db.AdminActorAsync();
        var branch = await _db.CreateBranchAsync("B-4A1", "فرع التأسيس");
        var mainWarehouse = await _db.CreateWarehouseAsync("W-MAIN-4A1", "مخزن رئيسي", branch);
        var consumables = await _db.CreateWarehouseAsync("W-CON-4A1", "مخزن مستهلكات", branch);

        await using (var connection = new NpgsqlConnection(TestDb.AdminConnection))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync(
                "UPDATE warehouses SET warehouse_type = 'CONSUMABLES' WHERE id = @id",
                new { id = consumables });
        }

        await Assert.ThrowsAsync<AppException>(async () =>
            await _db.RecipeService().SaveBranchSettingsAsync(actor, branch, mainWarehouse));

        var saved = await _db.RecipeService().SaveBranchSettingsAsync(actor, branch, consumables);
        Assert.Equal(consumables, saved.ConsumablesWarehouseId);

        var options = await _db.RecipeService().ConsumableWarehousesAsync(actor, branch);
        Assert.Contains(options, w => w.Id == consumables);
    }
}

