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
            if (ex.MessageText == "LAST_SUPER_ADMIN")
                return new ValidationAppException("validation.last_super_admin");
        }

        if (ex.SqlState == PostgresErrorCodes.NotNullViolation)
            return new ValidationAppException("validation.required_fields");

        return new AppException("error.database", 500);
    }
}
