using UnityEngine;

namespace TSKHook;

public class Window
{
    public static void Init()
    {
        Screen.SetResolution(TSKConfig.width, TSKConfig.height, false, TSKConfig.FPS);
        Plugin.Global.Log.LogInfo("Game window size: " + TSKConfig.width + "x" + TSKConfig.height);
    }
}