using Dapper;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IComplaintRepository
{
    Task<Guid> CreateAsync(ComplaintInput input, Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<Complaint>> ListAsync(Guid branchId, DateTime? from, DateTime? to,
        string? status, int limit = 300, CancellationToken ct = default);
    Task<bool> CloseAsync(Guid id, Guid userId, CancellationToken ct = default);
}

/// <summary>شكاوى العملاء مع الموظف المتسبب — 0086.</summary>
public sealed class ComplaintRepository : IComplaintRepository
{
    private readonly IDbConnectionFactory _factory;
    public ComplaintRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<Guid> CreateAsync(ComplaintInput input, Guid userId,
        CancellationToken ct = default)
    {
        if (input.BranchId == Guid.Empty || string.IsNullOrWhiteSpace(input.Description))
            throw new ValidationAppException("validation.required_fields");
        await using var connection = await _factory.OpenAsync(ct);
        var id = Guid.CreateVersion7();
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO complaints
                (id, branch_id, customer_id, employee_id, doc_date, description, status, created_by)
            VALUES (@id, @branchId, @customerId, @employeeId, @docDate, @description, 'OPEN', @userId)
            """, new { id, branchId = input.BranchId, customerId = input.CustomerId,
                employeeId = input.EmployeeId, docDate = input.DocDate.Date,
                description = input.Description, userId }, cancellationToken: ct));
        return id;
    }

    public async Task<IReadOnlyList<Complaint>> ListAsync(Guid branchId, DateTime? from,
        DateTime? to, string? status, int limit = 300, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return (await connection.QueryAsync<Complaint>(new CommandDefinition("""
            SELECT cp.id AS Id, cp.branch_id AS BranchId, b.name_ar AS BranchName,
                   cp.customer_id AS CustomerId, c.name_ar AS CustomerName,
                   cp.employee_id AS EmployeeId, e.name_ar AS EmployeeName,
                   cp.doc_date AS DocDate, cp.description AS Description, cp.status AS Status,
                   cp.created_at AS CreatedAt, cp.closed_at AS ClosedAt
            FROM complaints cp
            JOIN branches b ON b.id = cp.branch_id
            LEFT JOIN customers c ON c.id = cp.customer_id
            LEFT JOIN employees e ON e.id = cp.employee_id
            WHERE cp.branch_id = @branchId
              AND (@from::date IS NULL OR cp.doc_date >= @from)
              AND (@to::date IS NULL OR cp.doc_date <= @to)
              AND (@status IS NULL OR cp.status = @status)
            ORDER BY cp.doc_date DESC, cp.created_at DESC
            LIMIT @limit
            """, new { branchId, from, to, status, limit }, cancellationToken: ct))).ToList();
    }

    public async Task<bool> CloseAsync(Guid id, Guid userId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE complaints SET status = 'CLOSED', closed_at = now(), closed_by = @userId
            WHERE id = @id AND status = 'OPEN'
            """, new { id, userId }, cancellationToken: ct)) > 0;
    }
}
