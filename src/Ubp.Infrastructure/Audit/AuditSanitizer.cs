using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ubp.Infrastructure.Audit;

public static class AuditSanitizer
{
    private static readonly HashSet<string> Forbidden = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "password_hash", "passwordhash", "token_hash", "tokenhash",
        "refresh_token", "refreshtoken", "access_token", "accesstoken", "secret"
    };

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static string? ToJson(object? value)
    {
        if (value is null) return null;
        var node = JsonSerializer.SerializeToNode(value, value.GetType(), Options);
        if (node is null) return null;
        RemoveSecrets(node);
        return node.ToJsonString(Options);
    }

    private static void RemoveSecrets(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in obj.Select(p => p.Key).ToList())
            {
                if (Forbidden.Contains(key)) obj.Remove(key);
                else if (obj[key] is JsonNode child) RemoveSecrets(child);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
                if (child is not null) RemoveSecrets(child);
        }
    }
}
