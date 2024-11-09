using System.Text.Json;
using Il2CppSystem.IO;
using Il2CppSystem.Text;

namespace TSKHook;

public class TSKConfig
{
    public static double Speed;
    public static int FPS;
    public static bool TranslationEnabled;
    public static int width;
    public static int height;

    public static void Read()
    {
        if (File.Exists("./BepInEx/plugins/config.json"))
        {
            var content = File.InternalReadAllText("./BepInEx/plugins/config.json", Encoding.UTF8);
            var doc = JsonDocument.Parse(content);
            var config = doc.RootElement;

            var needWrite = false;

            if (config.TryGetProperty("speed", out var sValue))
            {
                Speed = sValue.GetDouble();
            }
            else
            {
                Speed = 0.5;
                needWrite = true;
            }

            if (config.TryGetProperty("fps", out var fValue))
            {
                FPS = fValue.GetInt32();
            }
            else
            {
                FPS = 60;
                needWrite = true;
            }

            if (config.TryGetProperty("translation", out var tValue))
            {
                TranslationEnabled = tValue.GetBoolean();
            }
            else
            {
                TranslationEnabled = true;
                needWrite = true;
            }

            if (config.TryGetProperty("width", out var wValue))
            {
                width = wValue.GetInt32();
            }
            else
            {
                width = 1280;
                needWrite = true;
            }

            if (config.TryGetProperty("height", out var hValue))
            {
                height = hValue.GetInt32();
            }
            else
            {
                height = 720;
                needWrite = true;
            }

            if (needWrite) WriteJsonFile(Speed, FPS, TranslationEnabled, width, width);

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
            FPS = 60;
            TranslationEnabled = true;
            width = 1280;
            height = 720;

            // Create default JSON file
            WriteJsonFile(0.5, 60, true, width, height);
        }
    }

    public static void WriteJsonFile(double speed, int fps, bool enabled, int w, int h)
    {
        var config = new config
        {
            speed = speed,
            fps = fps,
            translation = enabled,
            width = w,
            height = h
        };

        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText("./BepInEx/plugins/config.json", json);
    }

    public class config
    {
        public double speed { get; set; }
        public int fps { get; set; }
        public bool translation { get; set; }
        public int width { get; set; }
        public int height { get; set; }
    }
}