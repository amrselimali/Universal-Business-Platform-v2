using Dapper;
using Ubp.Core.Models;
using Ubp.Core.Results;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class SalesFinanceRepository
{
    /// <summary>
    /// كشف عمولة شهري (D20): الأساس 1=صافي البند، 2=إجمالي البند، 3=المُحصّل من الفاتورة.
    /// الحساب قابل للتكرار (ON CONFLICT DO NOTHING) فإعادة التشغيل لا تضاعف الصفوف.
    /// </summary>
    public async Task<int> CalculateCommissionsAsync(CommissionInput input, Guid userId,
        CancellationToken ct = default)
    {
        if (input.Rate is < 0 or > 100)
            throw new ValidationAppException("validation.commission_rate");

        var month = new DateTime(input.Month.Year, input.Month.Month, 1);
        var next = month.AddMonths(1);

        await using var connection = await _factory.OpenAsync(ct);
        var basis = input.Basis ?? await connection.ExecuteScalarAsync<short>(new CommandDefinition(
            "SELECT commission_basis FROM app_settings LIMIT 1", cancellationToken: ct));

        return await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO employee_commissions (id, employee_id, document_id, document_line_id, basis,
                base_amount, rate, amount, period_month)
            SELECT gen_random_uuid(), b.assigned_employee_id, d.id, l.id, @basis,
                   ROUND(base.amount, 2), @rate,
                   ROUND(base.amount * @rate / 100.0, 2), @month::date
            FROM commercial_documents d
            JOIN commercial_document_lines l ON l.document_id = d.id
            JOIN bookings b ON b.id = d.booking_id AND b.assigned_employee_id IS NOT NULL
            CROSS JOIN LATERAL (
                SELECT CASE @basis
                         WHEN 1 THEN l.net_amount
                         WHEN 2 THEN l.total_amount
                         ELSE COALESCE((SELECT SUM(a.amount) FROM document_allocations a
                                        WHERE a.target_document_id = d.id), 0)
                       END AS amount
            ) base
            WHERE d.doc_type = 'sales_invoice' AND d.status = 'posted'
              AND d.doc_date >= @month::date AND d.doc_date < @next::date
            ON CONFLICT (employee_id, document_line_id, period_month) DO NOTHING
            """, new { basis, rate = input.Rate, month, next, userId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<CommissionRow>> ListCommissionsAsync(DateTime month,
        Guid? employeeId, CancellationToken ct = default)
    {
        var first = new DateTime(month.Year, month.Month, 1);
        await using var connection = await _factory.OpenAsync(ct);
        return (await connection.QueryAsync<CommissionRow>(new CommandDefinition("""
            SELECT m.id AS Id, m.employee_id AS EmployeeId, e.name_ar AS EmployeeName,
                   m.document_id AS DocumentId, d.doc_no AS DocNo, m.basis AS Basis,
                   m.base_amount AS BaseAmount, m.rate AS Rate, m.amount AS Amount,
                   m.period_month AS PeriodMonth
            FROM employee_commissions m
            JOIN employees e ON e.id = m.employee_id
            JOIN commercial_documents d ON d.id = m.document_id
            WHERE m.period_month = @first::date
              AND (@employeeId::uuid IS NULL OR m.employee_id = @employeeId::uuid)
            ORDER BY e.name_ar, d.doc_no
            """, new { first, employeeId }, cancellationToken: ct))).ToList();
    }
}
