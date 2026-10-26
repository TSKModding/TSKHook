using System;
using System.Buffers;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TSKHook;

public static class UiJsonTranslator
{
    public static bool TryTranslate(string apiName, string json,
        UiTranslationIndex translations,
        out string translatedJson, out int replacementCount, out int hashFallbackCount,
        ChineseScript script = ChineseScript.Original)
    {
        translatedJson = json;
        replacementCount = 0;
        hashFallbackCount = 0;
        if (string.IsNullOrWhiteSpace(json) || translations == null || translations.ExactCount == 0)
        {
            return false;
        }

        using var document = JsonDocument.Parse(json);
        var buffer = new ArrayBufferWriter<byte>(Encoding.UTF8.GetByteCount(json));
        using (var writer = new Utf8JsonWriter(buffer))
        {
            Rewrite(apiName, "$", document.RootElement,
                new Dictionary<string, string>(StringComparer.Ordinal), translations, writer,
                ref replacementCount, ref hashFallbackCount, script);
        }

        if (replacementCount == 0)
        {
            return false;
        }

        translatedJson = Encoding.UTF8.GetString(buffer.WrittenSpan);
        return true;
    }

    private static void Rewrite(string apiName, string path, JsonElement element,
        IReadOnlyDictionary<string, string> inheritedContext,
        UiTranslationIndex translations,
        Utf8JsonWriter writer, ref int replacementCount, ref int hashFallbackCount, ChineseScript script)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var objectContext = UiJsonContext.Extend(inheritedContext, element);
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    Rewrite(apiName, path + "." + property.Name, property.Value, objectContext,
                        translations, writer, ref replacementCount, ref hashFallbackCount, script);
                }
                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    Rewrite(apiName, path + "[]", item, inheritedContext,
                        translations, writer, ref replacementCount, ref hashFallbackCount, script);
                }
                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                var source = element.GetString();
                if (!CaptureTextFilter.IsSensitivePath(path) && !TranslationProtection.IsProtectedPath(apiName, path) && TryGetTranslation(apiName, path, source,
                        inheritedContext, translations, out var translation, out var usedHashFallback))
                {
                    writer.WriteStringValue(ChineseTextConverter.Convert(translation, script));
                    replacementCount++;
                    if (usedHashFallback)
                    {
                        hashFallbackCount++;
                    }
                }
                else
                {
                    writer.WriteStringValue(source);
                }
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static bool TryGetTranslation(string apiName, string path, string source,
        IReadOnlyDictionary<string, string> context,
        UiTranslationIndex translations,
        out string translation, out bool usedHashFallback)
    {
        translation = null;
        usedHashFallback = false;
        if (string.IsNullOrEmpty(source))
        {
            return false;
        }

        var sourceHash = Sha256(source);
        var descriptor = UiTextKeyBuilder.Build(apiName, path, source, sourceHash, context);
        return translations.TryGet(descriptor.Key, sourceHash, out translation, out usedHashFallback);
    }

    public static string Sha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

public sealed record UiRuntimeTranslation(string SourceHash, string Translation,
    bool RenderedOnly = false, string SourceTemplate = null);
