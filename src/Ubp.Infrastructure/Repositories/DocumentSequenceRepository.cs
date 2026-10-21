using System.Data;
using System.Text.RegularExpressions;
using Dapper;

namespace Ubp.Infrastructure.Repositories;

public interface IDocumentSequenceRepository
{
    /// <summary>
    /// يرجع الرقم التالي بالشكل PREFIX-YYYY-000001 لنوع المستند والفرع وسنة التاريخ.
    /// يجب استدعاؤه داخل نفس transaction حفظ المستند حتى لا تحدث فجوات ولا أرقام مكررة.
    /// </summary>
    Task<string> NextNumberAsync(IDbConnection connection, IDbTransaction transaction,
        string docType, Guid branchId, DateOnly docDate, string? prefix = null,
        CancellationToken ct = default);
}

public sealed partial class DocumentSequenceRepository : IDocumentSequenceRepository
{
    [GeneratedRegex("^[A-Z][A-Z0-9_]*$")]
    private static partial Regex DocTypePattern();

    public async Task<string> NextNumberAsync(IDbConnection connection, IDbTransaction transaction,
        string docType, Guid branchId, DateOnly docDate, string? prefix = null,
        CancellationToken ct = default)
    {
        if (!DocTypePattern().IsMatch(docType) || docType.Length > 30)
            throw new ArgumentException("Invalid document type code.", nameof(docType));

        const string sql = """
            SELECT next_document_number(@DocType::varchar, @BranchId, @DocDate::date, @Prefix::varchar)
            """;

        var number = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
            sql,
            new
            {
                DocType = docType,
                BranchId = branchId,
                DocDate = docDate.ToDateTime(TimeOnly.MinValue),
                Prefix = prefix
            },
            transaction,
            cancellationToken: ct));

        return number ?? throw new InvalidOperationException("next_document_number returned null.");
    }
}
