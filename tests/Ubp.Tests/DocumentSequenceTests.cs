using Ubp.Infrastructure.Repositories;
using Xunit;

namespace Ubp.Tests;

public sealed class DocumentSequenceTests : IClassFixture<TestDb>
{
    private readonly TestDb _db;
    private readonly DocumentSequenceRepository _repo = new();

    public DocumentSequenceTests(TestDb db) => _db = db;

    private async Task<string> NextAsync(Guid branchId, DateOnly date, string type = "STOCK_RECEIPT")
    {
        await using var connection = await _db.Factory.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        var number = await _repo.NextNumberAsync(connection, tx, type, branchId, date);
        await tx.CommitAsync();
        return number;
    }

    [Fact]
    public async Task Numbers_are_sequential_and_formatted()
    {
        var branch = await _db.CreateBranchAsync("SEQ-A", "فرع الترقيم أ");
        var date = new DateOnly(2026, 10, 21);

        Assert.Equal("STOCK_RECEIPT-2026-000001", await NextAsync(branch, date));
        Assert.Equal("STOCK_RECEIPT-2026-000002", await NextAsync(branch, date));
    }

    [Fact]
    public async Task Counter_is_separate_per_branch_year_and_type()
    {
        var b1 = await _db.CreateBranchAsync("SEQ-B1", "فرع ب1");
        var b2 = await _db.CreateBranchAsync("SEQ-B2", "فرع ب2");

        await NextAsync(b1, new DateOnly(2026, 1, 1));
        Assert.EndsWith("-000001", await NextAsync(b2, new DateOnly(2026, 1, 1)));
        Assert.EndsWith("-000001", await NextAsync(b1, new DateOnly(2027, 1, 1)));
        Assert.EndsWith("-000001", await NextAsync(b1, new DateOnly(2026, 1, 1), "STOCK_ISSUE"));
    }

    [Fact]
    public async Task Concurrent_requests_never_get_duplicate_numbers()
    {
        var branch = await _db.CreateBranchAsync("SEQ-C", "فرع التزامن");
        var date = new DateOnly(2026, 10, 21);

        var numbers = await Task.WhenAll(
            Enumerable.Range(0, 50).Select(_ => NextAsync(branch, date)));

        Assert.Equal(50, numbers.Distinct().Count());
        Assert.Equal(Enumerable.Range(1, 50), numbers.Select(n => int.Parse(n[^6..])).OrderBy(n => n));
    }

    [Fact]
    public async Task Rolled_back_transaction_does_not_consume_a_number()
    {
        var branch = await _db.CreateBranchAsync("SEQ-D", "فرع التراجع");
        var date = new DateOnly(2026, 10, 21);

        await using (var connection = await _db.Factory.OpenAsync())
        await using (var tx = await connection.BeginTransactionAsync())
        {
            await _repo.NextNumberAsync(connection, tx, "STOCK_RECEIPT", branch, date);
            await tx.RollbackAsync();
        }

        Assert.EndsWith("-000001", await NextAsync(branch, date));
    }

    [Fact]
    public async Task Invalid_document_type_is_rejected()
    {
        var branch = await _db.CreateBranchAsync("SEQ-E", "فرع الرفض");
        await using var connection = await _db.Factory.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _repo.NextNumberAsync(connection, tx, "stock_receipt", branch, new DateOnly(2026, 10, 21)));
    }
}
