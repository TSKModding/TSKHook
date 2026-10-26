using HarmonyLib;

namespace TSKHook;

internal static class UiRuntimeActivity
{
    private static readonly Dictionary<int,TSKBattleManager> Battles = new();
    internal static bool InBattle
    {
        get
        {
            foreach (var battle in Battles.Values) if (battle != null && battle.isActiveAndEnabled) return true;
            return false;
        }
    }

    [HarmonyPrefix, HarmonyPatch(typeof(TSKBattleManager), "Start")]
    private static void Enter(TSKBattleManager __instance) => Battles[__instance.GetInstanceID()] = __instance;

    [HarmonyPostfix, HarmonyPatch(typeof(TSKBattleManager), "OnDestroy")]
    private static void Leave(TSKBattleManager __instance)
    {
        Battles.Remove(__instance.GetInstanceID());
        UiImageTranslationService.RequestDiscovery();
    }
}
