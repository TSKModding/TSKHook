﻿using System.IO;
using BepInEx;
using HarmonyLib;
using TMPro;
using UnityEngine;
using Utage;
using UtageExtensions;

namespace TSKHook;

public class Patch
{
    private static string currentAdvId;
    public static string fontName = "notosanscjktc";
    public static Font TranslateFont;
    public static TMP_FontAsset TMPTranslateFont;

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
            if (TranslateFont == null && File.Exists($"{Paths.PluginPath}/font/{fontName}"))
            {
                var ab = AssetBundle.LoadFromFile($"{Paths.PluginPath}/font/{fontName}");
                TranslateFont = ab.LoadAsset(fontName).Cast<Font>();
                TMPTranslateFont = ab.LoadAsset(fontName + " SDF")?.TryCast<TMP_FontAsset>();
                ab.Unload(false);
            }

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
    public static void AdvIniPre(AdventureTitleBandView __instance)
    {
        if (!TSKConfig.TranslationEnabled)
        {
            return;
        }

        string value;
        if (Translation.chapterDicts.ContainsKey(currentAdvId) && Translation.chapterDicts[currentAdvId].TryGetValue(__instance.TitleText, out value))
        {
            __instance.TitleText = value.IsNullOrEmpty() ? __instance.TitleText : value;
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(AdventureTitleBandView), "Initialize")]
    public static void AdvInitPost(AdventureTitleBandView __instance)
    {
        if (!TSKConfig.TranslationEnabled)
        {
            return;
        }

        if (TMPTranslateFont != null)
        {
            __instance.upTextComp.text.font = TMPTranslateFont;
            __instance.donwTextComp.text.font = TMPTranslateFont;

            for (int i = 0; i < __instance.titleText.Length; i++)
            {
                __instance.titleText[i].font = TMPTranslateFont;
            }

            for (int i = 0; i < __instance.donwTextObject.Length; i++)
            {
                TKSTextTMPGUI component = __instance.donwTextObject[i].GetComponent<TKSTextTMPGUI>();
                component.text.font = TMPTranslateFont;
            }
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UguiNovelText), "OnEnable")]
    public static void FontPatch(ref UguiNovelText __instance)
    {
        if (!TSKConfig.TranslationEnabled)
        {
            return;
        }

        if (TranslateFont != null)
        {
            __instance.font = TranslateFont;
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
        if (Translation.chapterDicts.ContainsKey(currentAdvId) && Translation.chapterDicts[currentAdvId].TryGetValue(__result, out value))
        {
            __result = value.IsNullOrEmpty() ? __result : value;
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Screen), "SetResolution", new[] { typeof(int), typeof(int), typeof(bool) })]
    public static bool SetWindowSize(ref int width, ref int height, ref bool fullscreen)
    {
        return false;
    }
}