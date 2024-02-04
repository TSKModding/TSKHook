using HarmonyLib;

namespace TSKHook;

public class Patch
{
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
}