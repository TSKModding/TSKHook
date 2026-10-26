using System;
using System.Collections.Generic;
using System.Text.Json;

namespace TSKHook;

public static class UiJsonContext
{
    public static Dictionary<string, string> Extend(IReadOnlyDictionary<string, string> inherited,
        JsonElement element)
    {
        var context = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in inherited)
        {
            context[pair.Key] = pair.Value;
        }
        foreach (var property in element.EnumerateObject())
        {
            if (IsIdentityProperty(property.Name) && TryGetScalar(property.Value, out var value))
            {
                context[property.Name] = value;
            }
        }

        return context;
    }

    private static bool IsIdentityProperty(string propertyName)
    {
        var name = propertyName.ToLowerInvariant();
        if (name.Contains("user", StringComparison.Ordinal) || name.Contains("player", StringComparison.Ordinal) ||
            name.Contains("token", StringComparison.Ordinal))
        {
            return false;
        }

        return name is "id" or "lv" or "level" or "type" or "rarity" or "phase" or "category" ||
               name.EndsWith("_id", StringComparison.Ordinal) ||
               name.EndsWith("_lv", StringComparison.Ordinal) ||
               name.EndsWith("_type", StringComparison.Ordinal) ||
               name.EndsWith("_kind", StringComparison.Ordinal) ||
               name.EndsWith("_no", StringComparison.Ordinal);
    }

    private static bool TryGetScalar(JsonElement element, out string value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                value = element.GetString();
                return !string.IsNullOrEmpty(value) && value.Length <= 64;
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                value = element.GetRawText();
                return true;
            default:
                value = null;
                return false;
        }
    }
}
