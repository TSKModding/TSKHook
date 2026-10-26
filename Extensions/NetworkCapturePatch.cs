using HarmonyLib;
using TKS.Network.Domain;

namespace TSKHook;

[HarmonyPatch(typeof(TKS.Network.HttpClient), "FormatData",
    new[] { typeof(ApiName), typeof(string), typeof(string) })]
internal static class NetworkCapturePatch
{
    [HarmonyPostfix]
    private static void CaptureFormattedResponse(ApiName apiName, ref string __result)
    {
        var apiNameText = apiName.ToString();
        UiTextCaptureService.Observe(apiNameText, __result);
        __result = UiTranslationService.TranslateResponse(apiNameText, __result);
    }
}
