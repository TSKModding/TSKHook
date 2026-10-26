using HarmonyLib;
using Il2CppInterop.Runtime;
using Spine.Unity;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TSKHook;

internal static class UiImageEventPatch
{
    // Do not detour Image setters: they are also used during native UI construction/destruction.
    // Changes on already enabled images are picked up by the bounded component watcher.
    [HarmonyPostfix, HarmonyPatch(typeof(Image), "OnEnable")]
    private static void ImageEnabled(Image __instance) => UiImageTranslationService.Notify(__instance);
    [HarmonyPostfix, HarmonyPatch(typeof(SkeletonGraphic), "Initialize")]
    private static void SpineChanged(SkeletonGraphic __instance) => UiImageTranslationService.Notify(__instance);
    [HarmonyPostfix, HarmonyPatch(typeof(MaskableGraphic), "OnEnable")]
    private static void GraphicEnabled(MaskableGraphic __instance)
    {
        var graphic = __instance.TryCast<SkeletonGraphic>();
        if (graphic != null) UiImageTranslationService.Notify(graphic);
    }
    [HarmonyPostfix, HarmonyPatch(typeof(SceneManager), "Internal_SceneLoaded")]
    private static void SceneLoaded() => UiImageTranslationService.RequestDiscovery();
}
