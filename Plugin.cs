using Il2CppInterop.Runtime.Injection;
using MelonLoader;
using System;
using System.Text;
using UnityEngine;

[assembly: MelonInfo(typeof(TSKHook.Plugin), "TSKHook-melon", "1.1.6", "TSKHook")]

namespace TSKHook;

public class Plugin : MelonMod
{
    public override void OnInitializeMelon()
    {
        if (Console.LargestWindowWidth > 0)
        {
            Console.OutputEncoding = Encoding.UTF8;
        }

        var log = LoggerInstance;
        Global.Log = log;
        log.Msg($"Plugin TSKHook is loaded!");

        TSKConfig.Read();
        Window.Init();
        Translation.InitAsync().Wait();
        Patch.Initialize();

        ClassInjector.RegisterTypeInIl2Cpp<PluginBehavior>();
        GameObject melonModObject = new GameObject
        {
            hideFlags = HideFlags.HideAndDontSave,
            name = "keybinding"
        };
        melonModObject.AddComponent<PluginBehavior>();
        UnityEngine.Object.DontDestroyOnLoad(melonModObject);
    }

    public class Global
    {
        public static MelonLogger.Instance Log { get; set; }
    }
}