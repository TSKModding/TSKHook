using System.ComponentModel;
using System.IO.Compression;
using System.Text;

namespace TSKHook;

public enum ChineseScript
{
    [Description("保持译文原样")] Original,
    [Description("简体中文")] Simplified,
    [Description("繁体中文")] Traditional
}

/// <summary>Single BMP character mappings only; no phrase engine or native dependency.</summary>
public static class ChineseTextConverter
{
    private static readonly Lazy<(Dictionary<char, char> Simplified, Dictionary<char, char> Traditional)> Maps = new(Load);

    public static string Convert(string text, ChineseScript script)
    {
        if (string.IsNullOrEmpty(text) || script == ChineseScript.Original || !Enum.IsDefined(typeof(ChineseScript), script)) return text;
        var map = script == ChineseScript.Simplified ? Maps.Value.Simplified : Maps.Value.Traditional;
        char[] changed = null;
        for (var i = 0; i < text.Length; i++)
        {
            // Keep rich-text tags and format placeholders byte-for-byte, including attribute values.
            if (text[i] is '<' or '{')
            {
                var end = text.IndexOf(text[i] == '<' ? '>' : '}', i + 1);
                if (end >= 0) { i = end; continue; }
            }
            if (!map.TryGetValue(text[i], out var replacement) || replacement == text[i]) continue;
            changed ??= text.ToCharArray();
            changed[i] = replacement;
        }
        return changed == null ? text : new string(changed);
    }

    private static (Dictionary<char, char>, Dictionary<char, char>) Load()
    {
        using var resource = typeof(ChineseTextConverter).Assembly.GetManifestResourceStream("TSKHook.ChineseCharacters")
            ?? throw new InvalidDataException("Missing Chinese character map.");
        using var compressed = new GZipStream(resource, CompressionMode.Decompress);
        using var reader = new BinaryReader(compressed, Encoding.UTF8);
        Dictionary<char, char> Read()
        {
            var count = reader.ReadInt32();
            if (count < 0 || count > 10000) throw new InvalidDataException("Invalid character map.");
            var map = new Dictionary<char, char>(count);
            var sources = new char[count];
            var source = 0;
            for (var i = 0; i < count; i++) sources[i] = (char)(source += reader.ReadUInt16());
            for (var i = 0; i < count; i++) map.Add(sources[i], (char)reader.ReadUInt16());
            return map;
        }
        return (Read(), Read());
    }
}
