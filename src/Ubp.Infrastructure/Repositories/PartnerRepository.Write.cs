using System.Text.Json;
using Dapper;
using Ubp.Core.Models;

namespace Ubp.Infrastructure.Repositories;

public sealed partial class PartnerRepository
{
    /// <summary>
    /// الكود التالي للطرف داخل نطاق/فرع: أكبر كود رقمي + 1 (بدءًا من 1)،
    /// ويتجاهل الأكواد غير الرقمية القديمة — 0094.
    /// </summary>
    public async Task<string> NextCodeAsync(PartnerKind kind, string scopeType, Guid? branchId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<string>(new CommandDefinition($"""
            SELECT (COALESCE(MAX(NULLIF(regexp_replace(code, '[^0-9]', '', 'g'), '')::bigint), 0) + 1)::text
            FROM {Table(kind)}
            WHERE scope_type = @scopeType AND branch_id IS NOT DISTINCT FROM @branchId
            """, new { scopeType, branchId }, cancellationToken: ct)) ?? "1";
    }

    public async Task<Partner> CreateAsync(PartnerKind kind, PartnerInput input,
        CancellationToken ct = default)
    {
        var sql = $"""
            INSERT INTO {Table(kind)}
                (code, name_ar, name_en, category_id, scope_type, branch_id, phone, phone2,
                 whatsapp_phone, email, address, tax_number, credit_limit, notes, is_active,
                 custom_fields)
            VALUES
                (@Code, @NameAr, @NameEn, @CategoryId, @ScopeType, @BranchId, @Phone, @Phone2,
                 @WhatsAppPhone, @Email, @Address, @TaxNumber, @CreditLimit, @Notes, @IsActive,
                  CAST(@CustomFields AS jsonb))
            RETURNING id
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var id = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(sql, new
        {
            input.Code, input.NameAr, input.NameEn, input.CategoryId, input.ScopeType,
            input.BranchId, input.Phone, input.Phone2, input.WhatsAppPhone, input.Email,
            input.Address, input.TaxNumber,
            input.CreditLimit, input.Notes, input.IsActive,
            CustomFields = JsonSerializer.Serialize(input.CustomFields)
        }, cancellationToken: ct));
        return (await GetAsync(kind, id, ct))!;
    }

    public async Task<Partner?> UpdateAsync(PartnerKind kind, Guid id, int version, PartnerInput input,
        CancellationToken ct = default)
    {
        var sql = $"""
            UPDATE {Table(kind)} SET
                code = @Code, name_ar = @NameAr, name_en = @NameEn, category_id = @CategoryId,
                scope_type = @ScopeType, branch_id = @BranchId, phone = @Phone,
                phone2 = @Phone2, whatsapp_phone = @WhatsAppPhone, email = @Email,
                address = @Address, tax_number = @TaxNumber, credit_limit = @CreditLimit,
                notes = @Notes, is_active = @IsActive,
                custom_fields = CAST(@CustomFields AS jsonb)
            WHERE id = @id AND version = @version
            RETURNING id
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var updated = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(sql, new
        {
            id, version, input.Code, input.NameAr, input.NameEn, input.CategoryId, input.ScopeType,
            input.BranchId, input.Phone, input.Phone2, input.WhatsAppPhone, input.Email,
            input.Address, input.TaxNumber,
            input.CreditLimit, input.Notes, input.IsActive,
            CustomFields = JsonSerializer.Serialize(input.CustomFields)
        }, cancellationToken: ct));
        return updated is null ? null : await GetAsync(kind, id, ct);
    }

    public async Task<bool> DeleteAsync(PartnerKind kind, Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(
            $"DELETE FROM {Table(kind)} WHERE id = @id", new { id }, cancellationToken: ct)) > 0;
    }

    public async Task<Partner?> SetActiveAsync(PartnerKind kind, Guid id, bool isActive,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            $"UPDATE {Table(kind)} SET is_active = @isActive WHERE id = @id",
            new { id, isActive }, cancellationToken: ct));
        return rows == 0 ? null : await GetAsync(kind, id, ct);
    }

    public async Task<bool> PhoneExistsAsync(PartnerKind kind, string phone, Guid? excludeId,
        CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition($"""
            SELECT EXISTS (
                SELECT 1 FROM {Table(kind)}
                WHERE btrim(phone) = btrim(@phone)
                  AND (@excludeId::uuid IS NULL OR id <> @excludeId)
            )
            """, new { phone, excludeId }, cancellationToken: ct));
    }

    public async Task<PartnerCategory> CreateCategoryAsync(PartnerKind kind, PartnerCategoryInput input,
        CancellationToken ct = default)
    {
        var sql = $"""
            INSERT INTO {CategoryTable(kind)} (code, name_ar, name_en, is_active)
            VALUES (@Code, @NameAr, @NameEn, @IsActive)
            RETURNING id
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var id = await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(sql, input, cancellationToken: ct));
        return (await GetCategoryAsync(kind, id, ct))!;
    }

    public async Task<PartnerCategory?> UpdateCategoryAsync(PartnerKind kind, Guid id, int version,
        PartnerCategoryInput input, CancellationToken ct = default)
    {
        var sql = $"""
            UPDATE {CategoryTable(kind)} SET code = @Code, name_ar = @NameAr, name_en = @NameEn,
                                             is_active = @IsActive
            WHERE id = @id AND version = @version
            RETURNING id
            """;
        await using var connection = await _factory.OpenAsync(ct);
        var updated = await connection.ExecuteScalarAsync<Guid?>(new CommandDefinition(sql, new
        {
            id, version, input.Code, input.NameAr, input.NameEn, input.IsActive
        }, cancellationToken: ct));
        return updated is null ? null : await GetCategoryAsync(kind, id, ct);
    }

    public async Task<bool> DeleteCategoryAsync(PartnerKind kind, Guid id, CancellationToken ct = default)
    {
        await using var connection = await _factory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(
            $"DELETE FROM {CategoryTable(kind)} WHERE id = @id", new { id }, cancellationToken: ct)) > 0;
    }
}
