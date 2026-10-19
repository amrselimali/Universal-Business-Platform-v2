using Microsoft.Extensions.Logging.Abstractions;
using Ubp.Application.Services;
using Ubp.Infrastructure.Data;
using Ubp.Infrastructure.Migrations;
using Xunit;

namespace Ubp.Tests;

public sealed class QaEndToEndTests : IClassFixture<TestDb>
{
    private static readonly string QaConnection =
        Environment.GetEnvironmentVariable("UBP_QA_CONNECTION")
        ?? "Host=localhost;Port=5432;Database=ubp_qa;Username=postgres";

    private readonly TestDb _db;

    public QaEndToEndTests(TestDb db) => _db = db;

    [Fact]
    public async Task Qa_suite_runs_against_the_isolated_database_and_passes()
    {
        var options = new DatabaseOptions
        {
            ConnectionString = QaConnection,
            MigrationConnectionString = QaConnection,
            QaConnectionString = QaConnection
        };
        var report = new MigrationRunner(options).Run();
        Assert.True(report.Success, report.Error);

        var appOptions = new DatabaseOptions
        {
            ConnectionString = TestDb.AdminConnection,
            MigrationConnectionString = TestDb.AdminConnection,
            QaConnectionString = QaConnection
        };
        var service = new QaService(_db.QaRepo, _db.Permissions(),
            new QaConnectionFactory(appOptions), appOptions, NullLogger<QaService>.Instance);

        var actor = await _db.AdminActorAsync();
        var summary = await service.RunAsync(actor, Array.Empty<string>());

        Assert.Equal("Completed", summary.Status);
        Assert.Equal(summary.TotalCount, summary.PassedCount);
        Assert.True(summary.TotalCount >= 9);
    }
}
