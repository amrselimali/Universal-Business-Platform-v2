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

public sealed class TestDb
{
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
            new PasswordHasher(), new BootstrapOptions(), NullLogger<DataSeeder>.Instance);
        await seeder.SeedAsync();
    }

    public SettingsRepository SettingsRepo => new(Factory);
    public UserRepository UserRepo => new(Factory);
    public BranchRepository BranchRepo => new(Factory);
    public WarehouseRepository WarehouseRepo => new(Factory);
    public RoleRepository RoleRepo => new(Factory);
    public PermissionRepository PermissionRepo => new(Factory);
    public AuditRepository AuditRepo => new(Factory);
    public SessionRepository SessionRepo => new(Factory);
    public RefreshTokenRepository RefreshRepo => new(Factory);
    public CurrencyRepository CurrencyRepo => new(Factory);
    public CustomFieldRepository CustomFieldRepo => new(Factory);
    public QaRepository QaRepo => new(Factory);
    public UnitRepository UnitRepo => new(Factory);
    public ItemRepository ItemRepo => new(Factory);
    public ItemCategoryRepository ItemCategoryRepo => new(Factory);
    public PartnerRepository PartnerRepo => new(Factory);
    public PeriodRepository PeriodRepo => new(Factory);
    public StockDocumentRepository StockDocRepo => new(Factory);
    public StockPostingRepository StockPostingRepo => new(Factory);
    public StocktakeRepository StocktakeRepo => new(Factory);
    public StockReportRepository StockReportRepo => new(Factory);

    public PermissionService Permissions() => new(PermissionRepo, Cache);

    public SettingsService Settings() =>
        new(SettingsRepo, AuditRepo, Permissions(), Cache);

    public AuthService Auth(IAccessTokenIssuer? issuer = null) => new(
        UserRepo, SessionRepo, RefreshRepo, BranchRepo, Settings(), AuditRepo,
        issuer ?? new FakeTokenIssuer(), new PasswordHasher(),
        NullLogger<AuthService>.Instance);

    public BranchService BranchService() =>
        new(BranchRepo, Permissions(), new CustomFieldService(CustomFieldRepo, Permissions(),
            new AuditService(AuditRepo)), new AuditService(AuditRepo));

    public WarehouseService WarehouseService() =>
        new(WarehouseRepo, BranchRepo, Permissions(),
            new CustomFieldService(CustomFieldRepo, Permissions(), new AuditService(AuditRepo)),
            new AuditService(AuditRepo));

    public UserService UserService() => new(
        UserRepo, BranchRepo, WarehouseRepo, RoleRepo, PermissionRepo, SessionRepo, RefreshRepo,
        Permissions(), Settings(), new PasswordHasher(), new AuditService(AuditRepo));

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
        new AuditService(AuditRepo));

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

    public async Task<decimal> OnHandAsync(Guid itemId, Guid warehouseId) =>
        await StockReportRepo.GetOnHandAsync(itemId, warehouseId);

    public sealed class FakeTokenIssuer : IAccessTokenIssuer
    {
        public string Create(User user, Guid sessionId, Guid? branchId) => "test-access-token";
    }
}
