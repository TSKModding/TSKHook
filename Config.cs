using System.Text.Json;
using Il2CppSystem.IO;
using Il2CppSystem.Text;

namespace TSKHook;

public class TSKConfig
{
    public static double Speed;
    public static int FPS;
    public static bool TranslationEnabled;

    public static void Read()
    {
        if (File.Exists("./BepInEx/plugins/config.json"))
        {
            var content = File.InternalReadAllText("./BepInEx/plugins/config.json", Encoding.UTF8);
            var doc = JsonDocument.Parse(content);
            var config = doc.RootElement;
            Speed = config.GetProperty("speed").GetDouble();
            FPS = config.GetProperty("fps").GetInt32();
            TranslationEnabled = config.GetProperty("translation").GetBoolean();

            Plugin.Global.Log.LogInfo("Current setting:");
            Plugin.Global.Log.LogInfo("Game speed(each step): " + Speed);
            Plugin.Global.Log.LogInfo("FPS: " + FPS);
            Plugin.Global.Log.LogInfo("Translation: " + (TranslationEnabled ? "Enabled" : "Disabled"));
        }
        else
        {
            Plugin.Global.Log.LogWarning("config.json not found!!!");
            Plugin.Global.Log.LogWarning("Using default config.");
            Speed = 0.5;
        }
    }
}