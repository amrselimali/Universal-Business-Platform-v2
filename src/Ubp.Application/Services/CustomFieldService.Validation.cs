using System.Globalization;
using System.Text.RegularExpressions;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

public sealed partial class CustomFieldService
{
    private static readonly Regex FieldKeyPattern = new("^[a-z][a-z0-9_]*$", RegexOptions.Compiled);
    private static readonly HashSet<string> Entities = new(StringComparer.OrdinalIgnoreCase)
    { "branches", "warehouses" };
    private static readonly HashSet<string> Types = new(StringComparer.OrdinalIgnoreCase)
    { "text", "number", "date", "bool", "list" };

    private readonly ICustomFieldRepository _repository;
    private readonly PermissionService _permissions;
    private readonly AuditService _audit;

    public CustomFieldService(ICustomFieldRepository repository, PermissionService permissions,
        AuditService audit)
    {
        _repository = repository; _permissions = permissions; _audit = audit;
    }

    public async Task ValidateDefinitionAsync(CustomFieldInput input, CancellationToken ct = default)
    {
        if (!Entities.Contains(input.EntityName))
            throw new ValidationAppException("validation.entity_not_supported");
        if (!FieldKeyPattern.IsMatch(input.FieldKey))
            throw new ValidationAppException("validation.field_key_invalid");
        if (string.IsNullOrWhiteSpace(input.LabelAr))
            throw new ValidationAppException("validation.field_label_required");
        if (!Types.Contains(input.DataType))
            throw new ValidationAppException("validation.field_type_invalid");
        if (input.DataType == "list" && input.Options.Count == 0)
            throw new ValidationAppException("validation.field_options_required");
    }

    public async Task<Dictionary<string, object?>> ValidateValuesAsync(string entityName,
        IReadOnlyDictionary<string, object?> values, CancellationToken ct = default)
    {
        var definitions = await _repository.ListActiveAsync(entityName, ct);
        var result = new Dictionary<string, object?>();
        foreach (var definition in definitions)
        {
            values.TryGetValue(definition.FieldKey, out var raw);
            var text = raw?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                if (definition.IsRequired)
                    throw new ValidationAppException("validation.custom_field_required",
                        new Dictionary<string, string> { ["field"] = definition.LabelAr });
                continue;
            }

            result[definition.FieldKey] = definition.DataType switch
            {
                "number" => decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var number)
                    ? number : throw Invalid(definition),
                "date" => DateTime.TryParse(text, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal, out var date)
                    ? date : throw Invalid(definition),
                "bool" => bool.TryParse(text, out var flag)
                    ? flag : throw Invalid(definition),
                "list" => definition.Options.Contains(text)
                    ? text : throw Invalid(definition),
                _ => text
            };
        }
        return result;
    }

    private static ValidationAppException Invalid(CustomFieldDefinition definition) =>
        new("validation.custom_field_invalid",
            new Dictionary<string, string> { ["field"] = definition.LabelAr });
}
