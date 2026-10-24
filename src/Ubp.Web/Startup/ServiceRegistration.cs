using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Localization;
using Microsoft.IdentityModel.Tokens;
using MudBlazor;
using MudBlazor.Services;
using Ubp.Application.Contracts;
using Ubp.Application.Services;
using Ubp.Infrastructure.Data;
using Ubp.Infrastructure.Migrations;
using Ubp.Infrastructure.Repositories;
using Ubp.Infrastructure.Security;
using Ubp.Web.Endpoints;
using Ubp.Web.Middleware;
using Ubp.Web.Security;
using Ubp.Web.Services;

namespace Ubp.Web.Startup;

public static class ServiceRegistration
{
    public static IServiceCollection AddUbpServices(this IServiceCollection services,
        IConfiguration configuration)
    {
        var db = configuration.GetSection("Database").Get<DatabaseOptions>() ?? new DatabaseOptions();
        var jwt = configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
        var bootstrap = configuration.GetSection("Bootstrap").Get<BootstrapOptions>() ?? new BootstrapOptions();
        if (jwt.Key.Length < 32)
            throw new InvalidOperationException("JWT key must be at least 32 characters.");

        services.AddSingleton(db);
        services.AddSingleton(jwt);
        services.AddSingleton(bootstrap);
        services.AddSingleton<IDbConnectionFactory>(_ => new NpgsqlConnectionFactory(db));
        services.AddSingleton(_ => new QaConnectionFactory(db));
        services.AddSingleton<MigrationRunner>();
        services.AddSingleton<PasswordHasher>();
        services.AddSingleton<TokenService>();
        services.AddScoped<IAccessTokenIssuer, TokenIssuerAdapter>();
        services.AddScoped<DataSeeder>();
        services.AddMemoryCache();

        AddRepositories(services);
        AddApplicationServices(services);

        services.AddHttpContextAccessor();
        services.AddScoped<UserContext>();
        services.AddScoped<UiCulture>();
        services.AddSingleton<Translator>();
        services.AddTransient<MudLocalizer, UbpMudLocalizer>();

        services.AddMudServices(config =>
        {
            config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomStart;
            config.SnackbarConfiguration.NewestOnTop = true;
            config.SnackbarConfiguration.ShowCloseIcon = true;
            config.SnackbarConfiguration.VisibleStateDuration = 4000;
        });

        services.AddAuthentication("Smart")
            .AddPolicyScheme("Smart", "Cookie or Bearer", options =>
            {
                options.ForwardDefaultSelector = context =>
                    context.Request.Headers.ContainsKey("Authorization") ? "Bearer" : "UbpCookie";
            })
            .AddCookie("UbpCookie", options =>
            {
                options.Cookie.Name = "ubp_auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.LoginPath = "/login";
                options.AccessDeniedPath = "/forbidden";
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = false;
            })
            .AddJwtBearer("Bearer", options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        System.Text.Encoding.UTF8.GetBytes(jwt.Key)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
            });

        services.AddAuthorization();
        services.AddCascadingAuthenticationState();
        services.AddRazorComponents().AddInteractiveServerComponents();
        return services;
    }

    private static void AddRepositories(IServiceCollection services)
    {
        services.AddScoped<ISettingsRepository, SettingsRepository>();
        services.AddScoped<IBranchRepository, BranchRepository>();
        services.AddScoped<IWarehouseRepository, WarehouseRepository>();
        services.AddScoped<ICurrencyRepository, CurrencyRepository>();
        services.AddScoped<ICustomFieldRepository, CustomFieldRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IPermissionRepository, PermissionRepository>();
        services.AddScoped<IAuditRepository, AuditRepository>();
        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<ISchemaAdminRepository, SchemaAdminRepository>();
        services.AddScoped<IQaRepository, QaRepository>();
        services.AddScoped<IDocumentSequenceRepository, DocumentSequenceRepository>();
        services.AddScoped<IUnitRepository, UnitRepository>();
        services.AddScoped<IItemCategoryRepository, ItemCategoryRepository>();
        services.AddScoped<IItemRepository, ItemRepository>();
        services.AddScoped<IPartnerRepository, PartnerRepository>();
        services.AddScoped<IPeriodRepository, PeriodRepository>();
        services.AddScoped<IStockDocumentRepository, StockDocumentRepository>();
        services.AddScoped<IStockPostingRepository, StockPostingRepository>();
        services.AddScoped<IStocktakeRepository, StocktakeRepository>();
        services.AddScoped<IStockReportRepository, StockReportRepository>();
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<ICostCenterRepository, CostCenterRepository>();
        services.AddScoped<IJournalRepository, JournalRepository>();
        services.AddScoped<IJournalEntryRepository, JournalEntryRepository>();
        services.AddScoped<IJournalPostingRepository, JournalPostingRepository>();
        services.AddScoped<IAccountingSettingsRepository, AccountingSettingsRepository>();
        services.AddScoped<IAccountingReportRepository, AccountingReportRepository>();
        services.AddScoped<IStockAccountingRepository, StockAccountingRepository>();
        services.AddScoped<IEmployeeRepository, EmployeeRepository>();
        services.AddScoped<ITaxRepository, TaxRepository>();
        services.AddScoped<IPaymentTermRepository, PaymentTermRepository>();
        services.AddScoped<IPriceListRepository, PriceListRepository>();
        services.AddScoped<IServiceRecipeRepository, ServiceRecipeRepository>();
        services.AddScoped<IBranchSettingsRepository, BranchSettingsRepository>();
        services.AddScoped<ICommercialDocumentRepository, CommercialDocumentRepository>();
        services.AddScoped<ITreasuryRepository, TreasuryRepository>();
    }

    private static void AddApplicationServices(IServiceCollection services)
    {
        services.AddScoped<PermissionService>();
        services.AddScoped<SettingsService>();
        services.AddScoped<ISettingsService>(sp => sp.GetRequiredService<SettingsService>());
        services.AddScoped<AuditService>();
        services.AddScoped<CustomFieldService>();
        services.AddScoped<BranchService>();
        services.AddScoped<WarehouseService>();
        services.AddScoped<CurrencyService>();
        services.AddScoped<RoleService>();
        services.AddScoped<UserService>();
        services.AddScoped<AuthService>();
        services.AddScoped<AuditQueryService>();
        services.AddScoped<DatabaseAdminService>();
        services.AddScoped<QaService>();
        services.AddScoped<UnitService>();
        services.AddScoped<ItemCategoryService>();
        services.AddScoped<ItemService>();
        services.AddScoped<PartnerService>();
        services.AddScoped<PeriodService>();
        services.AddScoped<StockDocumentService>();
        services.AddScoped<StocktakeService>();
        services.AddScoped<StockReportService>();
        services.AddScoped<AccountService>();
        services.AddScoped<CostCenterService>();
        services.AddScoped<JournalService>();
        services.AddScoped<JournalEntryService>();
        services.AddScoped<AccountingSettingsService>();
        services.AddScoped<OpeningBalanceService>();
        services.AddScoped<StockAccountingService>();
        services.AddScoped<AccountingReportService>();
        services.AddScoped<MasterDataService>();
        services.AddScoped<RecipeService>();
        services.AddScoped<PostingReadinessService>();
        services.AddScoped<PurchaseInvoiceService>();
        services.AddScoped<TreasuryService>();
    }
}
