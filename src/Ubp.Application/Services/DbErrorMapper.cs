using Npgsql;
using Ubp.Core.Results;

namespace Ubp.Application.Services;

public static class DbErrorMapper
{
    public static AppException Map(PostgresException ex)
    {
        if (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            var constraint = ex.ConstraintName ?? "";
            if (constraint.Contains("barcode", StringComparison.OrdinalIgnoreCase))
                return new ValidationAppException("validation.duplicate_barcode");
            if (constraint.Contains("username", StringComparison.OrdinalIgnoreCase))
                return new ValidationAppException("validation.username_taken");
            if (constraint.Contains("code", StringComparison.OrdinalIgnoreCase))
                return new ValidationAppException("validation.duplicate_code");
            if (constraint.Contains("name_ar", StringComparison.OrdinalIgnoreCase))
                return new ValidationAppException("validation.duplicate_name");
            if (constraint.Contains("field", StringComparison.OrdinalIgnoreCase))
                return new ValidationAppException("validation.duplicate_field");
            return new ValidationAppException("validation.duplicate_value");
        }

        if (ex.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            return ex.MessageText switch
            {
                "BRANCH_IN_USE" => new ValidationAppException("validation.branch_in_use"),
                "WAREHOUSE_IN_USE" => new ValidationAppException("validation.warehouse_in_use"),
                "ACCOUNT_IN_USE" => new ValidationAppException("validation.account_in_use"),
                _ => new ValidationAppException("validation.related_record")
            };
        }

        if (ex.SqlState == PostgresErrorCodes.CheckViolation)
        {
            if (ex.MessageText.StartsWith("USER_WAREHOUSE_COVERAGE", StringComparison.Ordinal))
            {
                var names = ex.MessageText.Split(':', 2).ElementAtOrDefault(1)?.Trim() ?? "";
                return new ValidationAppException("validation.warehouse_not_in_branches",
                    new Dictionary<string, string> { ["warehouses"] = names });
            }

            var accounting = MapAccounting(ex.MessageText);
            if (accounting is not null) return accounting;

            if (ex.MessageText == "LAST_SUPER_ADMIN")
                return new ValidationAppException("validation.last_super_admin");
            if (ex.MessageText == "STOCKTAKE_REASON_REQUIRED")
                return new ValidationAppException("validation.stocktake_reason_required");
        }

        if (ex.SqlState == PostgresErrorCodes.NotNullViolation)
            return new ValidationAppException("validation.required_fields");

        return new AppException("error.database", 500);
    }

    /// <summary>تحويل رسائل قيود المحاسبة إلى مفاتيح ترجمة مفهومة للمستخدم.</summary>
    private static AppException? MapAccounting(string? message)
    {
        if (string.IsNullOrEmpty(message)) return null;
        if (message.StartsWith("PERIOD_CLOSED", StringComparison.Ordinal))
            return new ValidationAppException("validation.period_closed");

        var separator = message.IndexOf(':');
        var code = separator > 0 ? message[..separator] : message;
        var lineNo = separator > 0 ? message[(separator + 1)..].Trim() : "";

        return code switch
        {
            "ENTRY_NOT_BALANCED" or "ENTRY_TOTALS_MISMATCH" =>
                new ValidationAppException("validation.entry_not_balanced"),
            "ACCOUNT_IS_GROUP" => WithLine("validation.account_is_group", lineNo),
            "ACCOUNT_INACTIVE" => WithLine("validation.account_inactive", lineNo),
            "PARTY_REQUIRED" => WithLine("validation.party_required", lineNo),
            "COST_CENTER_REQUIRED" => WithLine("validation.cost_center_required", lineNo),
            "COST_CENTER_NOT_ALLOWED" => WithLine("validation.cost_center_not_allowed", lineNo),
            "ACCOUNT_TREE_CYCLE" or "COST_CENTER_TREE_CYCLE" =>
                new ValidationAppException("validation.tree_cycle"),
            "SYSTEM_ACCOUNT_IMMUTABLE" => new ValidationAppException("validation.system_account"),
            "JOURNAL_ENTRY_IMMUTABLE" => new ValidationAppException("validation.entry_immutable"),
            "ENTRY_NOT_DRAFT" => new ValidationAppException("validation.entry_not_draft"),
            "ENTRY_NOT_POSTED" => new ValidationAppException("validation.entry_not_posted"),
            "ENTRY_ALREADY_REVERSED" => new ValidationAppException("validation.entry_already_reversed"),
            "ENTRY_IS_REVERSAL" => new ValidationAppException("validation.entry_is_reversal"),
            _ => null
        };
    }

    private static ValidationAppException WithLine(string key, string lineNo) =>
        new(key, string.IsNullOrEmpty(lineNo)
            ? null
            : new Dictionary<string, string> { ["line"] = lineNo });
}
