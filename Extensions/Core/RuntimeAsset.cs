using System.Text.Json;
using System.Text.Json.Serialization;

namespace TSKHook;

/// <summary>Strict parsing shared by local reload, updater and workbench publication.</summary>
public static class RuntimeAsset
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static UiTranslationIndex Load(string path)
    {
        var rows = new Dictionary<string, UiRuntimeTranslation>(StringComparer.Ordinal);
        var lineNumber = 0;
        foreach (var line in File.ReadLines(path))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            RuntimeRow row;
            try { row = JsonSerializer.Deserialize<RuntimeRow>(line, JsonOptions); }
            catch (JsonException e) { throw new InvalidDataException($"Invalid translation JSON at line {lineNumber}.", e); }
            if (row?.SchemaVersion != 1 || string.IsNullOrWhiteSpace(row.Key) ||
                !IsSha256(row.SourceHash) || string.IsNullOrWhiteSpace(row.Translation))
                throw new InvalidDataException($"Invalid translation fields at line {lineNumber}.");
            if (row.SourceTemplate != null && (!row.RenderedOnly || UiJsonTranslator.Sha256(row.SourceTemplate) != row.SourceHash))
                throw new InvalidDataException($"Invalid rendered template at line {lineNumber}.");
            if (!rows.TryAdd(row.Key, new UiRuntimeTranslation(row.SourceHash, row.Translation, row.RenderedOnly, row.SourceTemplate)))
                throw new InvalidDataException($"Duplicate translation key at line {lineNumber}.");
        }
        if (rows.Count == 0) throw new InvalidDataException("Translation package is empty.");
        return new UiTranslationIndex(rows);
    }

    public static bool IsSha256(string value) => value?.Length == 64 &&
        value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}

public sealed class RuntimeRow
{
    public int SchemaVersion { get; set; } = 1;
    public string Key { get; set; }
    public string SourceHash { get; set; }
    public string Translation { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool RenderedOnly { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string SourceTemplate { get; set; }
}
