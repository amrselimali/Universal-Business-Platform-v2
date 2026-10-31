using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Infrastructure.Data;
using Ubp.Infrastructure.Migrations;
using Ubp.Infrastructure.Repositories;
using Ubp.Infrastructure.Security;

namespace Ubp.Tests;

/// <summary>
/// قاعدة بيانات معزولة تماماً لكل سيناريو تزامن: تُنشأ، تُرحَّل، تُبذَر، ثم تُحذف.
/// تمنع تلوث البيانات بين السيناريوهات وتسمح بتشغيل عمليات متزامنة كثيفة بلا تصادم
/// على <c>public</c> في قاعدة الاختبار المشتركة.
/// </summary>
public sealed class IsolatedDb : IAsyncDisposable
{
    private readonly string _name;

    public DatabaseOptions Options { get; }
    public NpgsqlConnectionFactory Factory { get; }

    private IsolatedDb(string name, DatabaseOptions options)
    {
        _name = name;
        Options = options;
        Factory = new NpgsqlConnectionFactory(options);
    }

    public static async Task<IsolatedDb> CreateAsync(string tag)
    {
        var name = $"ubp_c_{tag}_{Guid.NewGuid():N}"[..26].ToLowerInvariant();
        await using (var admin = new NpgsqlConnection(TestDb.AdminConnection))
        {
            await admin.OpenAsync();
            await admin.ExecuteAsync($"CREATE DATABASE \"{name}\"");
        }

        var options = new DatabaseOptions
        {
            ConnectionString = ForDatabase(name),
            MigrationConnectionString = ForDatabase(name),
            QaConnectionString = "",
            AutoMigrate = false
        };
        var report = new MigrationRunner(options).Run();
        if (!report.Success)
            throw new InvalidOperationException($"Isolated migrations failed: {report.Error}");

        var db = new IsolatedDb(name, options);
        var seeder = new DataSeeder(new SettingsRepository(db.Factory), new UserRepository(db.Factory),
            new PasswordHasher(), new BootstrapOptions { SuperAdminPassword = TestDb.AdminPassword },
            NullLogger<DataSeeder>.Instance);
        await seeder.SeedAsync();
        return db;
    }

    public async Task<Guid> CreateAdminAsync()
    {
        var user = await new UserRepository(Factory).GetAuthByUsernameAsync("admin")
                   ?? throw new InvalidOperationException("Seeded admin missing");
        return user.Id;
    }

    public async Task<(Guid Branch, Guid Warehouse)> CreateBranchWithWarehouseAsync(string suffix)
    {
        var branch = await new BranchRepository(Factory).CreateAsync(new BranchInput
        {
            Code = "ISO" + suffix, NameAr = "فرع " + suffix
        });
        var warehouse = await new WarehouseRepository(Factory).CreateAsync(new WarehouseInput
        {
            Code = "ISOW" + suffix, NameAr = "مخزن " + suffix,
            BranchIds = new List<Guid> { branch.Id }
        });
        return (branch.Id, warehouse.Id);
    }

    public async Task<Item> CreateItemAsync(string code, string nameAr)
    {
        var unit = await new UnitRepository(Factory).CreateAsync(new UnitInput
        {
            Code = "U" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(),
            NameAr = "وحدة " + nameAr
        });
        return await new ItemRepository(Factory).CreateAsync(new ItemInput
        {
            Code = code,
            NameAr = nameAr,
            BaseUnitId = unit.Id,
            Units = new List<ItemUnitInput> { new() { UnitId = unit.Id, ConversionFactor = 1 } }
        });
    }

    public async Task<int> ScalarAsync(string sql, object? parameters = null)
    {
        await using var connection = await Factory.OpenAsync();
        return await connection.ExecuteScalarAsync<int>(sql, parameters);
    }

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(TestDb.AdminConnection);
        await admin.OpenAsync();
        await admin.ExecuteAsync($"DROP DATABASE IF EXISTS \"{_name}\" WITH (FORCE)");
    }

    private static string ForDatabase(string database) =>
        new NpgsqlConnectionStringBuilder(TestDb.AdminConnection)
        {
            Database = database,
            // سقف اتصالات صغير: السيناريو يرسل عمليات متزامنة أكثر من عدد الاتصالات
            // فتتزاحم على البركة بدل استنفاد max_connections في خادم الاختبار.
            MaxPoolSize = 30
        }.ConnectionString;
}
