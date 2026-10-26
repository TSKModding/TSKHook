using System.IO;
using System.Threading;
using BepInEx;
using BepInEx.Configuration;

namespace TSKHook;

// Compatibility facade keeps upstream feature code unchanged while using one configuration source.
public static class TSKConfig
{
    public static PluginSettings Settings { get; private set; }
    private static int pending;
    public static double Speed => Settings.Speed.Value;
    public static int FPS => Settings.Fps.Value;
    public static bool TranslationEnabled { get => Settings.Translation.Value; set => Settings.Translation.Value = value; }
    public static int width => Settings.Width.Value;
    public static int height => Settings.Height.Value;
    public static float zoom => Settings.Zoom.Value;
    public static bool UiTranslationEnabled => Settings.UiTranslation.Value;
    public static bool UiCaptureEnabled => Settings.Capture.Value;
    public static int UiCaptureFlushSeconds => Settings.FlushSeconds.Value;

    public static void Initialize(ConfigFile config)
    {
        Settings = new PluginSettings(config, Path.Combine(Paths.PluginPath, "config.json"),
            message => Plugin.Global.Log.LogWarning(message));
        config.SettingChanged += (_, _) => Interlocked.Exchange(ref pending, 1);
    }

    public static void Read()
    {
        Settings.Reload();
        Interlocked.Exchange(ref pending, 1);
    }

    internal static bool ConsumeChange() => Interlocked.Exchange(ref pending, 0) != 0;
}
