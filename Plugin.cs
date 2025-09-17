using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using System;
using System.Text;

namespace TSKHook;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BasePlugin
{
    public override void Load()
    {
        if (Console.LargestWindowWidth > 0)
        {
            Console.OutputEncoding = Encoding.UTF8;
        }

        Global.Log = Log;
        Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");

        TSKConfig.Read();
        Window.Init();
        Translation.InitAsync().Wait();
        Patch.Initialize();

        AddComponent<PluginBehavior>();
    }

    public class Global
    {
        public static ManualLogSource Log { get; set; }
    }
}