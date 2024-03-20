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

            if (config.TryGetProperty("speed", out var sValue))
            {
                Speed = sValue.GetDouble();
            }

            if (config.TryGetProperty("fps", out var fValue))
            {
                FPS = fValue.GetInt32();
            }

            if (config.TryGetProperty("translation", out var tValue))
            {
                TranslationEnabled = tValue.GetBoolean();
            }

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