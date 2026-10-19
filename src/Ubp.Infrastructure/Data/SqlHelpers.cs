namespace Ubp.Infrastructure.Data;

public static class SqlSort
{
    /// <summary>
    /// يبني ORDER BY من قائمة أعمدة مسموحة فقط (حماية من الحقن) مع إضافة id لثبات الترتيب.
    /// </summary>
    public static string Build(string? requested, IReadOnlyDictionary<string, string> allowed,
        string fallback, bool descending)
    {
        var key = requested?.Trim().ToLowerInvariant();
        var column = fallback;
        if (!string.IsNullOrEmpty(key) && allowed.TryGetValue(key, out var mapped))
            column = mapped;

        var direction = descending ? "DESC" : "ASC";
        return $"{column} {direction}, id {direction}";
    }

    public static string BuildNoId(string? requested, IReadOnlyDictionary<string, string> allowed,
        string fallback, bool descending)
    {
        var key = requested?.Trim().ToLowerInvariant();
        var column = fallback;
        if (!string.IsNullOrEmpty(key) && allowed.TryGetValue(key, out var mapped))
            column = mapped;
        return $"{column} {(descending ? "DESC" : "ASC")}";
    }
}

public static class SearchSql
{
    /// <summary>يطبّع مصطلح البحث بنفس قواعد normalize_search داخل PostgreSQL.</summary>
    public const string Normalize = "normalize_search(@search)";

    public static string Contains(string column) => $"{column} LIKE '%' || {Normalize} || '%'";
}
