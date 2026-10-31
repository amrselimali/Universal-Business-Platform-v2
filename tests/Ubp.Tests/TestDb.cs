using Dapper;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ubp.Application.Contracts;
using Ubp.Application.Services;
using Ubp.Core.Models;
using Ubp.Core.Security;
using Ubp.Infrastructure.Data;
using Ubp.Infrastructure.Migrations;
using Ubp.Infrastructure.Repositories;
using Ubp.Infrastructure.Security;

namespace Ubp.Tests;

public sealed partial class TestDb
{
    public static readonly string AdminPassword = "Test-Admin!2026";

    public static readonly string AdminConnection =
        Environment.GetEnvironmentVariable("UBP_TEST_CONNECTION")
        ?? "Host=localhost;Port=5432;Database=ubp_test;Username=postgres";

    public DatabaseOptions Options { get; }
    public NpgsqlConnectionFactory Factory { get; }
    public IMemoryCache Cache { get; }

    public TestDb()
    {
        Options = new DatabaseOptions
        {
            ConnectionString = AdminConnection,
            MigrationConnectionString = AdminConnection,
            QaConnectionString = AdminConnection,
            AutoMigrate = false
        };
        Reset();
        var report = new MigrationRunner(Options).Run();
        if (!report.Success)
            throw new InvalidOperationException("Test migrations failed: " + report.Error);
        Factory = new NpgsqlConnectionFactory(Options);
        Cache = new MemoryCache(new MemoryCacheOptions());
        Seed().GetAwaiter().GetResult();
    }

    private void Reset()
    {
        using var connection = new NpgsqlConnection(AdminConnection);
        connection.Open();
        connection.Execute("DROP SCHEMA IF EXISTS public CASCADE; CREATE SCHEMA public;");
    }

    private async Task Seed()
    {
        var seeder = new DataSeeder(new SettingsRepository(Factory), new UserRepository(Factory),
            new PasswordHasher(), new BootstrapOptions { SuperAdminPassword = AdminPassword },
            NullLogger<DataSeeder>.Instance);
        await seeder.SeedAsync();
    }

    public async Task<ActorContext> AdminActorAsync()
    {
        var admin = await UserRepo.GetAuthByUsernameAsync("admin")
                    ?? throw new InvalidOperationException("Seed admin missing");
        return new ActorContext
        {
            UserId = admin.Id, Username = admin.Username, Name = admin.Name,
            IsAdmin = true, IsSuperAdmin = true, Language = "ar",
            AllowedBranchIds = Array.Empty<Guid>(), TraceId = "test-trace"
        };
    }

    public async Task<Guid> CreateBranchAsync(string code, string nameAr)
    {
        var branch = await BranchRepo.CreateAsync(new BranchInput { Code = code, NameAr = nameAr });
        return branch.Id;
    }

    public async Task<Guid> CreateWarehouseAsync(string code, string nameAr, params Guid[] branchIds)
    {
        var warehouse = await WarehouseRepo.CreateAsync(new WarehouseInput
        {
            Code = code, NameAr = nameAr, BranchIds = branchIds.ToList()
        });
        return warehouse.Id;
    }

    public async Task<Guid> CreateUserAsync(string username, string password, Guid[] branches,
        Guid[]? warehouses = null)
    {
        var users = UserService();
        var admin = await AdminActorAsync();
        var detail = await users.CreateAsync(admin, new UserInput
        {
            Username = username, Name = username, Password = password, IsActive = true,
            BranchIds = branches.ToList(), WarehouseIds = (warehouses ?? Array.Empty<Guid>()).ToList(),
            DefaultBranchId = branches.FirstOrDefault()
        });
        return detail.User.Id;
    }

    public CustomFieldService CustomFields() => new(
        CustomFieldRepo, Permissions(), new AuditService(AuditRepo));

    public ItemService ItemService() => new(
        ItemRepo, UnitRepo, ItemCategoryRepo, CustomFields(), Permissions(),
        new AuditService(AuditRepo));

    public StocktakeService StocktakeService() => new(
        StocktakeRepo, StockDocRepo, StockPostingRepo, ItemRepo, WarehouseRepo, Settings(),
        Permissions(), new AuditService(AuditRepo));

    public PeriodService PeriodService() =>
        new(PeriodRepo, BranchRepo, Permissions(), new AuditService(AuditRepo));

    public StockDocumentService StockDocumentService() => new(
        StockDocRepo, StockPostingRepo, ItemRepo, WarehouseRepo, Settings(), Permissions(),
        new AuditService(AuditRepo), StockAccountingService());

    public AccountService AccountService() =>
        new(AccountRepo, Permissions(), new AuditService(AuditRepo));

    public CostCenterService CostCenterService() =>
        new(CostCenterRepo, Permissions(), new AuditService(AuditRepo));

    public JournalService JournalService() =>
        new(JournalRepo, Permissions(), new AuditService(AuditRepo));

    public JournalEntryService JournalEntryService() => new(
        JournalEntryRepo, JournalPostingRepo, AccountRepo, JournalRepo, Permissions(),
        new AuditService(AuditRepo));

    public AccountingSettingsService AccountingSettings() => new(
        AccountingSettingsRepo, AccountRepo, JournalRepo, AccountService(), Permissions(),
        new AuditService(AuditRepo));

    public OpeningBalanceService OpeningBalanceService() => new(
        JournalEntryRepo, JournalPostingRepo, JournalRepo, AccountRepo, CostCenterRepo,
        PartnerRepo, JournalEntryService(), Permissions(), new AuditService(AuditRepo));

    public StockAccountingService StockAccountingService() => new(
        StockAccountingRepo, JournalEntryRepo, JournalPostingRepo, AccountingSettingsRepo,
        Permissions(), new AuditService(AuditRepo));

    public AccountingReportService AccountingReports() => new(AccountingReportRepo, Permissions());

    public MasterDataService MasterData() => new(EmployeeRepo, TaxRepo, PaymentTermRepo,
        PriceListRepo, Permissions(), new AuditService(AuditRepo));

    public RecipeService RecipeService() => new(RecipeRepo, ItemRepo, BranchSettingsRepo,
        WarehouseRepo, Permissions(), new AuditService(AuditRepo));

    public PostingReadinessService Readiness() =>
        new(AccountingSettingsRepo, AccountRepo, Cache);

    /// <summary>يحمّل شجرة الحسابات القياسية وحسابات الترحيل ويعيد خريطة بالكود.</summary>
    public async Task<Dictionary<string, Account>> SeedAccountingAsync()
    {
        var actor = await AdminActorAsync();
        await AccountingSettings().LoadDefaultTreeAsync(actor);
        var accounts = await AccountRepo.ListAllAsync(true);
        return accounts.ToDictionary(a => a.Code, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<Guid> JournalIdByCodeAsync(string code)
    {
        var journal = await JournalRepo.GetByCodeAsync(code)
                      ?? throw new InvalidOperationException("Journal missing: " + code);
        return journal.Id;
    }

    /// <summary>ينشئ قيداً متوازناً ويرحّله ويعيد رأسه.</summary>
    public async Task<JournalEntry> PostBalancedEntryAsync(Guid branchId, DateTime date,
        Guid debitAccount, Guid creditAccount, decimal amount, string journalCode = "GEN",
        Guid? customerId = null, Guid? supplierId = null, Guid? costCenterId = null)
    {
        var actor = await AdminActorAsync();
        var detail = await JournalEntryService().CreateAsync(actor, new JournalEntryInput
        {
            JournalId = await JournalIdByCodeAsync(journalCode),
            BranchId = branchId,
            EntryDate = date,
            Description = "قيد اختبار",
            Lines =
            {
                new JournalEntryLineInput
                {
                    AccountId = debitAccount, Debit = amount, CustomerId = customerId,
                    SupplierId = supplierId, CostCenterId = costCenterId
                },
                new JournalEntryLineInput
                {
                    AccountId = creditAccount, Credit = amount, CustomerId = customerId,
                    SupplierId = supplierId, CostCenterId = costCenterId
                }
            }
        });
        return await JournalEntryService().PostAsync(actor, detail.Header.Id);
    }

    public async Task<Item> CreateItemAsync(string code, string nameAr)
    {
        var unit = await UnitRepo.CreateAsync(new UnitInput
        {
            Code = "U" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(),
            NameAr = "وحدة " + nameAr
        });
        return await ItemRepo.CreateAsync(new ItemInput
        {
            Code = code,
            NameAr = nameAr,
            BaseUnitId = unit.Id,
            Units = new List<ItemUnitInput> { new() { UnitId = unit.Id, ConversionFactor = 1 } }
        });
    }

    public async Task<StockDocument> PostReceiptAsync(Item item, Guid branchId, Guid warehouseId,
        decimal quantity, decimal cost, bool allowNegative = false)
    {
        var actor = await AdminActorAsync();
        var draft = await StockDocRepo.CreateDraftAsync(new StockDocumentInput
        {
            DocType = StockDocumentTypes.Receipt,
            DocDate = DateTime.UtcNow.Date,
            BranchId = branchId,
            WarehouseId = warehouseId,
            Lines =
            {
                new StockDocumentLineInput
                {
                    ItemId = item.Id, UnitId = item.BaseUnitId, ConversionFactor = 1,
                    Quantity = quantity, UnitCost = cost
                }
            }
        }, actor.UserId);
        return await StockPostingRepo.PostAsync(draft.Id, actor.UserId, allowNegative);
    }

    public async Task<StockDocument> PostReceiptWithLotAsync(Item item, Guid branchId,
        Guid warehouseId, decimal quantity, decimal cost, string? lotNumber, DateTime? expiry)
    {
        var actor = await AdminActorAsync();
        var draft = await StockDocRepo.CreateDraftAsync(new StockDocumentInput
        {
            DocType = StockDocumentTypes.Receipt,
            DocDate = DateTime.UtcNow.Date,
            BranchId = branchId,
            WarehouseId = warehouseId,
            Lines =
            {
                new StockDocumentLineInput
                {
                    ItemId = item.Id, UnitId = item.BaseUnitId, ConversionFactor = 1,
                    Quantity = quantity, UnitCost = cost, LotNumber = lotNumber, ExpiryDate = expiry
                }
            }
        }, actor.UserId);
        return await StockPostingRepo.PostAsync(draft.Id, actor.UserId, false);
    }

    public async Task<decimal> OnHandAsync(Guid itemId, Guid warehouseId) =>
        await StockReportRepo.GetOnHandAsync(itemId, warehouseId);

    public sealed class FakeTokenIssuer : IAccessTokenIssuer
    {
        public string Create(User user, Guid sessionId, Guid? branchId) => "test-access-token";
    }
}
