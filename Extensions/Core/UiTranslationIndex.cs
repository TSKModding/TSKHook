using System;
using System.Collections.Generic;

namespace TSKHook;

public sealed class UiTranslationIndex
{
    public static readonly UiTranslationIndex Empty =
        new(new Dictionary<string, UiRuntimeTranslation>(StringComparer.Ordinal));

    private readonly IReadOnlyDictionary<string, UiRuntimeTranslation> exact;
    private readonly IReadOnlyDictionary<string, UiRuntimeTranslation> uniqueBySourceHash;
    private readonly IReadOnlyDictionary<string, UiRuntimeTranslation> renderedBySourceHash;
    private readonly UiTextFormatPattern[] renderedPatterns;

    public UiTranslationIndex(IReadOnlyDictionary<string, UiRuntimeTranslation> translations)
    {
        exact = translations ?? new Dictionary<string, UiRuntimeTranslation>(StringComparer.Ordinal);

        uniqueBySourceHash = BuildUnique(exact.Values, false, out var ambiguousCount);
        renderedBySourceHash = BuildUnique(exact.Values, true, out _);
        var patterns = new List<UiTextFormatPattern>();
        foreach (var row in exact.Values)
        {
            if (row.SourceTemplate == null) continue;
            if (!row.RenderedOnly) throw new ArgumentException("Text templates must be rendered-only.");
            patterns.Add(new UiTextFormatPattern(row.SourceTemplate, row.Translation));
            if (patterns.Count > 64) throw new ArgumentException("At most 64 rendered templates are supported.");
        }
        renderedPatterns = patterns.ToArray();
        AmbiguousSourceHashCount = ambiguousCount;
    }

    private static Dictionary<string, UiRuntimeTranslation> BuildUnique(
        IEnumerable<UiRuntimeTranslation> rows, bool rendered, out int ambiguousCount)
    {
        var unique = new Dictionary<string, UiRuntimeTranslation>(StringComparer.Ordinal);
        var ambiguous = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in rows)
        {
            if (entry.SourceTemplate != null || (!rendered && entry.RenderedOnly) ||
                string.IsNullOrEmpty(entry.SourceHash) || ambiguous.Contains(entry.SourceHash))
            {
                continue;
            }

            if (!unique.TryGetValue(entry.SourceHash, out var existing))
            {
                unique[entry.SourceHash] = entry;
                continue;
            }

            if (!string.Equals(existing.Translation, entry.Translation, StringComparison.Ordinal))
            {
                unique.Remove(entry.SourceHash);
                ambiguous.Add(entry.SourceHash);
            }
        }

        ambiguousCount = ambiguous.Count;
        return unique;
    }

    public int ExactCount => exact.Count;

    public int UniqueSourceHashCount => uniqueBySourceHash.Count;

    public int AmbiguousSourceHashCount { get; }

    public bool TryGetRenderedText(string source, out string translation)
    {
        translation = null;
        if (TranslationProtection.IsProtectedSource(source)) return false;
        if (renderedBySourceHash.TryGetValue(UiJsonTranslator.Sha256(source), out var exactRow))
        {
            translation = exactRow.Translation;
            return !string.IsNullOrWhiteSpace(translation);
        }
        foreach (var pattern in renderedPatterns)
        {
            if (!pattern.TryTranslate(source, out var candidate)) continue;
            if (translation != null && translation != candidate) { translation = null; return false; }
            translation = candidate;
        }
        return translation != null;
    }

    public bool TryGetUniqueBySourceHash(string sourceHash, out string translation)
    {
        translation = null;
        if (TranslationProtection.IsProtectedSourceHash(sourceHash)) return false;
        if (!uniqueBySourceHash.TryGetValue(sourceHash, out var entry) ||
            string.IsNullOrWhiteSpace(entry.Translation))
        {
            return false;
        }

        translation = entry.Translation;
        return true;
    }

    public bool TryGet(string key, string sourceHash, out string translation, out bool usedHashFallback)
    {
        usedHashFallback = false;
        translation = null;

        if (TranslationProtection.IsProtectedSourceHash(sourceHash)) return false;

        if (exact.TryGetValue(key, out var exactEntry) && !exactEntry.RenderedOnly &&
            string.Equals(exactEntry.SourceHash, sourceHash, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(exactEntry.Translation))
        {
            translation = exactEntry.Translation;
            return true;
        }

        if (!TryGetUniqueBySourceHash(sourceHash, out translation))
        {
            return false;
        }

        usedHashFallback = true;
        return true;
    }
}
