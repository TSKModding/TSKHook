using HarmonyLib;
using Utage;
using UtageExtensions;

namespace TSKHook;

public class Patch
{
    private static string currentAdvId;

    public static void Initialize()
    {
        Harmony.CreateAndPatchAll(typeof(Patch));
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(GameConfig), "set_FixedFrameRate")]
    public static void set_FixedFrameRate(ref int value)
    {
        if (TSKConfig.FPS > 60)
        {
            value = TSKConfig.FPS;
            Plugin.Global.Log.LogInfo("FPS setting was overridden: " + value);
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(AdvDataManager), "DownloadChaperKeyFileUsed")]
    public static void DownloadChaperKeyFileUsed(ref string scenarioLabel)
    {
        if (!TSKConfig.TranslationEnabled)
        {
            return;
        }

        if (scenarioLabel != null)
        {
            currentAdvId = scenarioLabel.ToLower();
            if (!Translation.chapterDicts.ContainsKey(currentAdvId))
            {
                Translation.FetchChapterTranslation(currentAdvId);
            }
            Plugin.Global.Log.LogInfo(scenarioLabel);
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(AdventureTitleBandView), "Initialize")]
    public static void AdvInit(AdventureTitleBandView __instance)
    {
        if (!TSKConfig.TranslationEnabled)
        {
            return;
        }

        string value;
        if (Translation.chapterDicts[currentAdvId].TryGetValue(__instance.TitleText, out value))
        {
            __instance.TitleText = value.IsNullOrEmpty() ? __instance.TitleText : value;
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(AdvPage), "get_NameText")]
    public static void get_NameText(ref string __result)
    {
        if (!TSKConfig.TranslationEnabled)
        {
            return;
        }

        string value;
        if (Translation.nameDicts.TryGetValue(__result, out value))
        {
            __result = value.IsNullOrEmpty() ? __result : value;
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(AdvBacklog), "get_MainCharacterNameText")]
    public static void get_MainCharacterNameText(ref string __result)
    {
        if (!TSKConfig.TranslationEnabled)
        {
            return;
        }

        string value;
        if (Translation.nameDicts.TryGetValue(__result, out value))
        {
            __result = value.IsNullOrEmpty() ? __result : value;
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(LanguageManagerBase), "ParseCellLocalizedTextBySwapDefaultLanguage")]
    public static void ParseCellLocalizedTextBySwapDefaultLanguage(ref StringGridRow row, ref string defaultColumnName,
        ref string __result)
    {
        if (!TSKConfig.TranslationEnabled)
        {
            return;
        }

        string value;
        if (Translation.chapterDicts[currentAdvId].TryGetValue(__result, out value))
        {
            __result = value.IsNullOrEmpty() ? __result : value;
        }
    }
}