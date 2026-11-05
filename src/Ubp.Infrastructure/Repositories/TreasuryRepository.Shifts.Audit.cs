using Dapper;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class TreasuryRepository
{
    public async Task<IReadOnlyList<ShiftReviewItem>> ListReviewItemsAsync(Guid shiftId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return (await QueryReviewItemsAsync(connection, shiftId, ct)).ToList();
    }

    private static async Task<IReadOnlyList<ShiftReviewItem>> QueryReviewItemsAsync(
        NpgsqlConnection connection, Guid shiftId, CancellationToken ct) =>
        (await connection.QueryAsync<ShiftReviewItem>(new CommandDefinition("""
            SELECT i.id AS Id, i.shift_id AS ShiftId, i.screen_code AS ScreenCode,
                   i.entity_type AS EntityType, i.entity_id AS EntityId, i.doc_no AS DocNo,
                   i.doc_date AS DocDate, i.party_name AS PartyName, i.amount AS Amount,
                   i.doc_status AS DocStatus, i.status AS Status, i.reviewed_at AS ReviewedAt,
                   u.username AS ReviewedBy
            FROM shift_review_items i
            LEFT JOIN users u ON u.id = i.reviewed_by
            WHERE i.shift_id = @shiftId
            ORDER BY i.doc_date, i.doc_no, i.entity_id
            """, new { shiftId }, cancellationToken: ct))).ToList();

    /// <summary>اعتماد مراجعة الوردية بواسطة يوزر محاسب (صلاحية APPROVE على شاشة الورديات).</summary>
    public async Task<Shift> ReviewShiftAsync(Guid shiftId, Guid userId, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var status = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT status FROM cashier_shifts WHERE id = @shiftId FOR UPDATE",
            new { shiftId }, tx, cancellationToken: ct)) ?? throw new NotFoundAppException();
        if (status == ShiftStatuses.Open)
            throw new ValidationAppException("validation.shift_not_closed");

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE shift_review_items
            SET status = 'reviewed', reviewed_at = now(), reviewed_by = @userId
            WHERE shift_id = @shiftId AND status = 'pending'
            """, new { shiftId, userId }, tx, cancellationToken: ct));

        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE cashier_shifts
            SET review_status = 'reviewed', reviewed_at = now(), reviewed_by = @userId,
                updated_by = @userId
            WHERE id = @shiftId
            """, new { shiftId, userId }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return (await GetShiftAsync(shiftId, ct))!;
    }
}
