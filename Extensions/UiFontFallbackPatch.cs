using System;
using System.Collections.Generic;
using System.Threading;
using HarmonyLib;
using TMPro;

namespace TSKHook;

internal static class UiFontFallbackPatch
{
    private static readonly object FontLock = new();
    private static readonly HashSet<int> ProcessedFontIds = new();
    private static readonly HashSet<int> UnreadableAtlasFontIds = new();
    private static int errorCount;
    private static int atlasGuardErrorCount;
    private static long skippedUnreadableAtlasInsertions;
    private static readonly Dictionary<int, ComponentText> Texts = new();
    private static readonly Queue<int> TextOrder = new();
    private static readonly BoundedWorkQueue<int, TMP_Text> Refresh = new(4096);
    private static readonly Func<string,string> Translate = source => UiTranslationService.TryTranslateRenderedText(source,out var value) ? value : null;
    private static (int Revision, bool Enabled, ChineseScript Script) lastSettings;
    [ThreadStatic] private static bool writing;
    private sealed record ComponentText(TMP_Text Text, RenderedTextState State);
    private static bool TextEnabled => TSKConfig.TranslationEnabled && TSKConfig.UiTranslationEnabled;

    private static RenderedTextState State(TMP_Text text)
    {
        var id = text.GetInstanceID();
        if (Texts.TryGetValue(id,out var value) && value.Text == text) return value.State;
        if (!Texts.ContainsKey(id))
        {
            if (Texts.Count >= 4096)
            {
                var oldest = TextOrder.Dequeue();
                if (Texts.Remove(oldest,out var evicted) && evicted.Text != null)
                {
                    // Do not leave an evicted component displaying a translation whose original we can no longer restore.
                    if (evicted.State.Resolve(evicted.Text.text,UiTranslationService.RenderedRevision,false,TSKConfig.Settings.Script.Value,Translate,out var original))
                    {
                        var previous = writing; writing = true;
                        try { evicted.Text.text = original; } finally { writing = previous; }
                    }
                }
            }
            TextOrder.Enqueue(id);
        }
        var state = new RenderedTextState();
        Texts[id] = new(text,state);
        return state;
    }

    internal static void Update()
    {
        var settings = (UiTranslationService.RenderedRevision,TextEnabled,TSKConfig.Settings.Script.Value);
        if (lastSettings != settings)
        {
            lastSettings = settings;
            foreach (var pair in Texts) if (pair.Value.Text != null) Refresh.Enqueue(pair.Key,pair.Value.Text);
        }
        var deadline = System.Diagnostics.Stopwatch.GetTimestamp() + (long)(System.Diagnostics.Stopwatch.Frequency * .0004);
        for (var i = 0; i < 32 && System.Diagnostics.Stopwatch.GetTimestamp() < deadline && Refresh.TryDequeue(out var text); i++)
            if (text != null) TranslateCurrentText(text);
    }

    internal static int ProcessedFontCount
    {
        get
        {
            lock (FontLock)
            {
                return ProcessedFontIds.Count;
            }
        }
    }

    internal static long SkippedUnreadableAtlasInsertions =>
        Interlocked.Read(ref skippedUnreadableAtlasInsertions);

    [HarmonyPrefix]
    [HarmonyPatch(typeof(TMP_FontAsset), nameof(TMP_FontAsset.TryAddCharacterInternal))]
    private static bool SkipUnreadableAtlasInsertion(
        TMP_FontAsset __instance,
        ref TMP_Character character,
        ref bool __result)
    {
        if (!TSKConfig.TranslationEnabled || !TSKConfig.UiTranslationEnabled || __instance == null)
        {
            return true;
        }

        try
        {
            var atlasTexture = __instance.atlasTexture;
            if (atlasTexture == null || atlasTexture.isReadable)
            {
                return true;
            }

            character = null;
            __result = false;
            Interlocked.Increment(ref skippedUnreadableAtlasInsertions);

            var fontId = __instance.GetInstanceID();
            lock (FontLock)
            {
                if (UnreadableAtlasFontIds.Add(fontId))
                {
                    Plugin.Global.Log.LogInfo(
                        $"[UI Translation] Skipping failed glyph insertion for unreadable TMP atlas: {__instance.name}.");
                }
            }

            return false;
        }
        catch (Exception exception)
        {
            if (Interlocked.Increment(ref atlasGuardErrorCount) <= 5)
            {
                Plugin.Global.Log.LogWarning(
                    $"[UI Translation] Could not inspect TMP atlas before glyph insertion: {exception}");
            }

            return true;
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(TMP_Text), "set_text")]
    private static void TextChanged(TMP_Text __instance)
    {
        Apply(__instance);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(TMP_Text), "set_text")]
    private static void TranslateText(TMP_Text __instance, ref string value)
    {
        if (writing || __instance == null || value?.Length > 4096) return;
        try
        {
            if (State(__instance).Resolve(value,UiTranslationService.RenderedRevision,TextEnabled,TSKConfig.Settings.Script.Value,Translate,out var translated)) value = translated;
        }
        catch (Exception e) { if (Interlocked.Increment(ref errorCount) <= 5) Plugin.Global.Log.LogWarning("[UI Translation] Text fallback: " + e.Message); }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(TextMeshProUGUI), "OnEnable")]
    private static void UguiEnabled(TextMeshProUGUI __instance)
    {
        TranslateCurrentText(__instance);
        Apply(__instance);
    }

    // SetText overloads and native field writes can bypass set_text. Translate the final value
    // before TMP builds the mesh; no scene scans or permanent per-frame polling are needed.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(TextMeshProUGUI), "OnPreRenderCanvas")]
    private static void BeforeUguiRender(TextMeshProUGUI __instance)
    {
        TranslateCurrentText(__instance);
        Apply(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(TextMeshPro), "OnEnable")]
    private static void MeshEnabled(TextMeshPro __instance)
    {
        TranslateCurrentText(__instance);
        Apply(__instance);
    }

    private static void TranslateCurrentText(TMP_Text text)
    {
        if (writing || text == null) return;
        try
        {
            var current = text.text;
            if (current?.Length > 4096) return;
            if (!State(text).Resolve(current,UiTranslationService.RenderedRevision,TextEnabled,TSKConfig.Settings.Script.Value,Translate,out var value)) return;
            writing = true;
            text.text = value;
        }
        catch (Exception e) { if (Interlocked.Increment(ref errorCount) <= 5) Plugin.Global.Log.LogWarning("[UI Translation] Text refresh: " + e.Message); }
        finally { writing = false; }
    }

    private static void Apply(TMP_Text text)
    {
        if (!TSKConfig.TranslationEnabled || !TSKConfig.UiTranslationEnabled || text == null ||
            Patch.TMPTranslateFont == null)
        {
            return;
        }

        try
        {
            var font = text.font;
            if (font == null || font == Patch.TMPTranslateFont)
            {
                return;
            }

            var fontId = font.GetInstanceID();
            lock (FontLock)
            {
                if (ProcessedFontIds.Contains(fontId))
                {
                    return;
                }

                var fallbacks = font.fallbackFontAssetTable;
                if (fallbacks == null)
                {
                    fallbacks = new Il2CppSystem.Collections.Generic.List<TMP_FontAsset>();
                    font.fallbackFontAssetTable = fallbacks;
                }

                if (!fallbacks.Contains(Patch.TMPTranslateFont))
                {
                    fallbacks.Add(Patch.TMPTranslateFont);
                }

                ProcessedFontIds.Add(fontId);
            }
        }
        catch (Exception exception)
        {
            if (Interlocked.Increment(ref errorCount) <= 5)
            {
                Plugin.Global.Log.LogWarning($"[UI Translation] Could not configure TMP font fallback: {exception}");
            }
        }
    }
}
