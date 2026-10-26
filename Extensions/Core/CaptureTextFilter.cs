using System;

namespace TSKHook;

public static class CaptureTextFilter
{
    public static bool ShouldCapture(string path, string value)
    {
        return !string.IsNullOrWhiteSpace(value) && !IsSensitivePath(path) && ContainsJapaneseText(value);
    }

    private static bool ContainsJapaneseText(string value)
    {
        foreach (var character in value)
        {
            if (character is >= '\u3040' and <= '\u30ff' or >= '\u3400' and <= '\u4dbf' or >= '\u4e00' and <= '\u9fff')
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsSensitivePath(string path)
    {
        var normalized = path.ToLowerInvariant();
        return normalized.StartsWith("$.user_data", StringComparison.Ordinal) ||
               normalized.Contains(".user_data.", StringComparison.Ordinal) ||
               normalized.EndsWith(".user_nm", StringComparison.Ordinal) ||
               normalized.EndsWith(".user_name", StringComparison.Ordinal) ||
               normalized.EndsWith(".player_name", StringComparison.Ordinal) ||
               normalized.EndsWith(".nickname", StringComparison.Ordinal) ||
               normalized.EndsWith(".comment", StringComparison.Ordinal) ||
               normalized.EndsWith(".user_comment", StringComparison.Ordinal) ||
               normalized.EndsWith(".message", StringComparison.Ordinal) ||
               normalized.EndsWith(".profile_text", StringComparison.Ordinal) ||
               normalized.EndsWith(".introduction", StringComparison.Ordinal);
    }
}
