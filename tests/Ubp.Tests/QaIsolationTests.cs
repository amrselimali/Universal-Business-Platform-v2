using Microsoft.Extensions.Logging.Abstractions;
using Ubp.Application.Services;
using Ubp.Core.Results;
using Ubp.Infrastructure.Data;
using Xunit;

namespace Ubp.Tests;

public sealed class QaIsolationTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;

    public QaIsolationTests(TestDb db) => _db = db;

    [Fact]
    public async Task Qa_run_fails_closed_when_target_database_is_the_application_database()
    {
        var options = new DatabaseOptions
        {
            ConnectionString = TestDb.AdminConnection,
            MigrationConnectionString = TestDb.AdminConnection,
            QaConnectionString = TestDb.AdminConnection
        };
        var service = new QaService(_db.QaRepo, _db.Permissions(),
            new QaConnectionFactory(options), options, NullLogger<QaService>.Instance);
        var actor = await _db.AdminActorAsync();

        var exception = await Assert.ThrowsAsync<ValidationAppException>(async () =>
            await service.RunAsync(actor, Array.Empty<string>()));
        Assert.Equal("qa.isolation_required", exception.Code);
    }

    [Fact]
    public async Task Qa_test_registry_is_discoverable()
    {
        var actor = await _db.AdminActorAsync();
        var service = new QaService(_db.QaRepo, _db.Permissions(),
            new QaConnectionFactory(new DatabaseOptions { QaConnectionString = TestDb.AdminConnection }),
            new DatabaseOptions { QaConnectionString = TestDb.AdminConnection },
            NullLogger<QaService>.Instance);
        var matrix = await service.GetMatrixAsync(actor);
        Assert.True(matrix.Count >= 8);
        Assert.Contains(matrix, x => x.Definition.Code == "PERMISSIONS_DEFAULT_DENY");
    }
}
