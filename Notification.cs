using System.Diagnostics;

namespace TSKHook;

public class Notification
{
    public static void Popup(string title, string text)
    {
        var script = string.Format("$headlineText = '{0}';", title) +
                     string.Format("$bodyText = '{0}';", text) +
                     "$ToastText02 = [Windows.UI.Notifications.ToastTemplateType, Windows.UI.Notifications, ContentType = WindowsRuntime]::ToastText02;" +
                     "$TemplateContent = [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime]::GetTemplateContent($ToastText02);" +
                     "$TemplateContent.SelectSingleNode('//text[@id=\"1\"]').InnerText = $headlineText;" +
                     "$TemplateContent.SelectSingleNode('//text[@id=\"2\"]').InnerText = $bodyText;" +
                     "$AppId = 'Twinkle Star Knight';" +
                     "[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier($AppId).Show($TemplateContent);";

        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            Arguments = script
        };
        Process.Start(start);
    }

    public static void SsPopup(string location)
    {
        var scriptArgs = "-ExecutionPolicy Bypass -F ./BepInEx/plugins/SS_Notification.ps1 " + location + "";

        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            Arguments = scriptArgs
        };
        Process.Start(start);
    }
}