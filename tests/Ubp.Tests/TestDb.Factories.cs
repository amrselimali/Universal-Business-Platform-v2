using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Ubp.Application.Contracts;
using Ubp.Application.Services;
using Ubp.Infrastructure.Migrations;
using Ubp.Infrastructure.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Tests;

public sealed partial class TestDb
{
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
    public AccountRepository AccountRepo => new(Factory);
    public CostCenterRepository CostCenterRepo => new(Factory);
    public JournalRepository JournalRepo => new(Factory);
    public JournalEntryRepository JournalEntryRepo => new(Factory);
    public JournalPostingRepository JournalPostingRepo => new(Factory);
    public AccountingSettingsRepository AccountingSettingsRepo => new(Factory);
    public AccountingReportRepository AccountingReportRepo => new(Factory);
    public StockAccountingRepository StockAccountingRepo => new(Factory);
    public EmployeeRepository EmployeeRepo => new(Factory);
    public TaxRepository TaxRepo => new(Factory);
    public PaymentTermRepository PaymentTermRepo => new(Factory);
    public PriceListRepository PriceListRepo => new(Factory);
    public ServiceRecipeRepository RecipeRepo => new(Factory);
    public BranchSettingsRepository BranchSettingsRepo => new(Factory);
    public CommercialDocumentRepository CommercialRepo => new(Factory);
    public TreasuryRepository TreasuryRepo => new(Factory);
    public BookingRepository BookingRepo => new(Factory);
    public AppointmentRepository AppointmentRepo => new(Factory);
    public DeliveryRepository DeliveryRepo => new(Factory);
    public SalesInvoiceRepository SalesRepo => new(Factory);
    public SalesFinanceRepository SalesFinanceRepo => new(Factory);
    public PosRepository PosRepo => new(Factory);
    public FollowUpRepository FollowUpRepo => new(Factory);

    public TreasuryService TreasuryService() => new(
        TreasuryRepo, AccountRepo, PartnerRepo, Permissions(), new AuditService(AuditRepo));

    public BookingService BookingService() => new(
        BookingRepo, Settings(), Permissions(), new AuditService(AuditRepo));

    public SalesFulfillmentService Fulfillment() => new(
        DeliveryRepo, AppointmentRepo, Settings(), Permissions(), new AuditService(AuditRepo));

    public SalesInvoiceService SalesInvoices() => new(
        SalesRepo, CommercialRepo, Permissions(), new AuditService(AuditRepo));

    public SalesFinanceService SalesFinance() => new(
        SalesFinanceRepo, CommercialRepo, Permissions(), new AuditService(AuditRepo));

    public PosService Pos() => new(
        PosRepo, Settings(), Permissions(), new AuditService(AuditRepo));

    public FollowUpService FollowUps() => new(
        FollowUpRepo, Permissions(), new AuditService(AuditRepo));

    public PermissionService Permissions() => new(PermissionRepo, Cache);

    public DashboardService Dashboard() =>
        new(new DashboardRepository(Factory), Permissions());

    public SystemHealthService SystemHealth() =>
        new(new SystemHealthRepository(Factory), new BackupLogRepository(Factory),
            new Ubp.Infrastructure.Migrations.MigrationRunner(Options), Permissions());

    public ImportService Imports() =>
        new(new ImportJobRepository(Factory), OpeningBalanceService(), Permissions());

    public ReservationService Reservations() =>
        new(new ReservationRepository(Factory), TreasuryRepo, PartnerRepo, TreasuryService(),
            BookingService(), Permissions());

    public BranchAccountDefaultsService BranchDefaults() =>
        new(new BranchAccountDefaultsRepository(Factory), AccountRepo, Permissions());

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
}
