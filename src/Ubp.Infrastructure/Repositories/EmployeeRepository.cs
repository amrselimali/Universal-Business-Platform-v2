using Dapper;
using Ubp.Core.Models;
using Ubp.Core.Paging;
using Ubp.Infrastructure.Data;

namespace Ubp.Infrastructure.Repositories;

public interface IEmployeeRepository
{
    Task<PagedResult<Employee>> ListAsync(PageRequest request, bool includeInactive, CancellationToken ct = default);
    Task<IReadOnlyList<Employee>> ListOptionsAsync(CancellationToken ct = default);
    Task<Employee?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Employee?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<Guid> CreateAsync(Guid id, EmployeeInput input, Guid userId, CancellationToken ct = default);
    Task<Employee?> UpdateAsync(Guid id, int version, EmployeeInput input, Guid userId, CancellationToken ct = default);
    Task<Employee?> SetActiveAsync(Guid id, bool isActive, Guid userId, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed class EmployeeRepository : IEmployeeRepository
{
    private const string Columns = """
        e.id AS Id, e.code AS Code, e.name_ar AS NameAr, e.name_en AS NameEn,
        e.phone AS Phone, e.email AS Email, e.user_id AS UserId,
        e.role_id AS RoleId, (SELECT r.name_ar FROM roles r WHERE r.id = e.role_id) AS RoleName,
        e.job_title AS JobTitle,
        e.is_booking_receiver AS IsBookingReceiver, e.is_seller AS IsSeller,
        e.is_service_executor AS IsServiceExecutor, e.hire_date AS HireDate,
        e.treatment_account_id AS TreatmentAccountId,
        e.branch_id AS BranchId,
        (SELECT b.name_ar FROM branches b WHERE b.id = e.branch_id) AS BranchName,
        e.notes AS Notes, e.is_active AS IsActive, e.version AS Version
        """;

    private static readonly IReadOnlyDictionary<string, string> SortMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        { ["code"] = "e.code", ["name_ar"] = "e.name_ar" };

    private readonly IDbConnectionFactory _factory;
    public EmployeeRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<PagedResult<Employee>> ListAsync(PageRequest request, bool includeInactive,
        CancellationToken ct = default)
    {
        var where = new List<string>();
        if (!includeInactive) where.Add("e.is_active");
        if (!string.IsNullOrWhiteSpace(request.Search)) where.Add(SearchSql.Contains("e.search_text"));
        var whereSql = where.Count == 0 ? "WHERE true" : "WHERE " + string.Join(" AND ", where);
        var order = SqlSort.Build(request.SortBy, SortMap, "e.code", request.Descending);
        var sql = $"""
            SELECT {Columns} FROM employees e {whereSql}
            ORDER BY {order} LIMIT @take OFFSET @offset;
            SELECT count(*) FROM employees e {whereSql};
            """;
        await using var connection = await _factory.OpenAsync(ct);
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, new
        { search = request.Search, take = request.SafePageSize, offset = request.Offset },
            cancellationToken: ct));
        var items = (await multi.ReadAsync<Employee>()).ToList();
        var total = await multi.ReadFirstAsync<int>();
        return new PagedResult<Employee>
        { Items = items, Total = total, Page = request.SafePage, PageSize = request.SafePageSize };
    }

    public async Task<IReadOnlyList<Employee>> ListOptionsAsync(CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.QueryAsync<Employee>(new CommandDefinition(
            $"SELECT {Columns} FROM employees e " +
            "WHERE e.is_active AND e.is_service_executor ORDER BY e.name_ar",
            cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<Employee?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<Employee>(new CommandDefinition(
            $"SELECT {Columns} FROM employees e WHERE e.id = @id", new { id }, cancellationToken: ct));
    }

    public async Task<Employee?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<Employee>(new CommandDefinition(
            $"SELECT {Columns} FROM employees e WHERE e.code = @code", new { code }, cancellationToken: ct));
    }

    public async Task<Guid> CreateAsync(Guid id, EmployeeInput i, Guid userId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition("""
            INSERT INTO employees (id, code, name_ar, name_en, phone, email, user_id,
                role_id, job_title, is_booking_receiver, is_seller, is_service_executor, hire_date,
                treatment_account_id, branch_id, notes, is_active, created_by)
            VALUES (@id, @Code, @NameAr, @NameEn, @Phone, @Email, @UserId, @RoleId, @JobTitle,
                    @IsBookingReceiver, @IsSeller, @IsServiceExecutor, @HireDate,
                    @TreatmentAccountId, @BranchId, @Notes, @IsActive, @userId)
            RETURNING id
            """, new { id, i.Code, i.NameAr, i.NameEn, i.Phone, i.Email, i.UserId, i.RoleId,
                i.JobTitle, i.IsBookingReceiver, i.IsSeller, i.IsServiceExecutor, i.HireDate,
                i.TreatmentAccountId, i.BranchId, i.Notes, i.IsActive, userId },
            cancellationToken: ct));
    }

    public async Task<Employee?> UpdateAsync(Guid id, int version, EmployeeInput i, Guid userId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var updated = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition("""
            UPDATE employees SET code = @Code, name_ar = @NameAr, name_en = @NameEn, phone = @Phone,
                email = @Email, user_id = @UserId, role_id = @RoleId, job_title = @JobTitle,
                is_booking_receiver = @IsBookingReceiver, is_seller = @IsSeller,
                is_service_executor = @IsServiceExecutor, hire_date = @HireDate,
                treatment_account_id = @TreatmentAccountId, branch_id = @BranchId, notes = @Notes,
                is_active = @IsActive, updated_by = @userId
            WHERE id = @id AND version = @version RETURNING id
            """, new { id, version, i.Code, i.NameAr, i.NameEn, i.Phone, i.Email, i.UserId,
                i.RoleId, i.JobTitle, i.IsBookingReceiver, i.IsSeller, i.IsServiceExecutor,
                i.HireDate, i.TreatmentAccountId, i.BranchId, i.Notes, i.IsActive, userId },
            cancellationToken: ct));
        return updated is null ? null : await GetAsync(id, ct);
    }

    public async Task<Employee?> SetActiveAsync(Guid id, bool isActive, Guid userId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE employees SET is_active = @isActive, updated_by = @userId WHERE id = @id",
            new { id, isActive, userId }, cancellationToken: ct));
        return rows == 0 ? null : await GetAsync(id, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM employees WHERE id = @id", new { id }, cancellationToken: ct)) > 0;
    }
}

