using System.IO;
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
    public static AssetBundle fontBundle;
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
            if ((TranslateFont == null || TMPTranslateFont == null) && File.Exists($"{Paths.PluginPath}/font/{fontName}"))
            {
                if (fontBundle == null)
                {
                    fontBundle = AssetBundle.LoadFromFile($"{Paths.PluginPath}/font/{fontName}");
                }
                TranslateFont = fontBundle.LoadAsset(fontName).Cast<Font>();
                TMPTranslateFont = fontBundle.LoadAsset(fontName + " SDF").TryCast<TMP_FontAsset>();
                fontBundle.Unload(false);

                if (TranslateFont != null && TMPTranslateFont != null)
                {
                    Plugin.Global.Log.LogInfo("Font loaded.");
                }
            }

            currentAdvId = scenarioLabel.ToLower();
            if (!Translation.chapterDicts.ContainsKey(currentAdvId))
            {
                Translation.FetchChapterTranslationAsync(currentAdvId).Wait();
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

    [HarmonyPrefix]
    [HarmonyPatch(typeof(MaximizeCharaView), "SetCharaRoot_Scale")]
    public static void SetCharaRoot_Scale(ref MaximizeZoomEventData _zoomData)
    {
        if (_zoomData.PinchData > 0)
        {
            _zoomData.PinchData = TSKConfig.zoom;
        }
        else
        {
            _zoomData.PinchData = -TSKConfig.zoom;
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(MaximizeCharaView), "initialize")]
    public static void CharaViewInit(ref MaximizeCharaView __instance)
    {
        __instance.maximizeMinSize = 0.1f;
    }
}