using System;
using UnityEngine;
using Utage;

namespace TSKHook;

public class PluginBehavior : MonoBehaviour
{
    private static readonly float WaitTime = 1.0f;
    private static readonly float CtrlWaitTime = 0.1f;
    public static bool IsGameSpeedChanged { get; set; }
    public static float CurrentGameSpeed { get; set; }
    private static float LastGSExecuteTime { get; set; }
    private static float LastFPSExecuteTime { get; set; }
    private static float LastSkipExecuteTime { get; set; }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F8))
        {
            CurrentGameSpeed = Time.timeScale + (float)TSKConfig.Speed;
            Time.timeScale += (float)TSKConfig.Speed;
            IsGameSpeedChanged = (int)Time.timeScale != 1;
            LastGSExecuteTime = Time.deltaTime;
            var currSpeed = Time.timeScale.ToString();
            var text = "Game speed increased. Current: " + currSpeed + "x";
            Plugin.Global.Log.LogInfo(text);

            Notification.Popup("Game Speed", text);
        }

        if (Input.GetKeyDown(KeyCode.F7))
        {
            CurrentGameSpeed = Time.timeScale - (float)TSKConfig.Speed;
            Time.timeScale -= (float)TSKConfig.Speed;
            IsGameSpeedChanged = (int)Time.timeScale != 1;
            LastGSExecuteTime = Time.deltaTime;
            var currSpeed = Time.timeScale.ToString();
            var text = "Game speed decreased. Current: " + currSpeed + "x";
            Plugin.Global.Log.LogInfo(text);

            Notification.Popup("Game Speed", text);
        }

        if (Input.GetKeyDown(KeyCode.F6))
        {
            CurrentGameSpeed = 1.0f;
            Time.timeScale = 1.0f;
            IsGameSpeedChanged = (int)Time.timeScale != 1;
            var currSpeed = Time.timeScale.ToString();
            var text = "Game speed restored. Current: " + currSpeed + "x";
            Plugin.Global.Log.LogInfo(text);

            Notification.Popup("Game Speed", text);
        }

        if (Input.GetKeyDown(KeyCode.F5))
        {
            CurrentGameSpeed = 0.0f;
            Time.timeScale = 0.0f;
            IsGameSpeedChanged = (int)Time.timeScale != 1;
            LastGSExecuteTime = Time.deltaTime;
            var currSpeed = Time.timeScale.ToString();
            var text = "Game speed freezed. Current: " + currSpeed + "x";
            Plugin.Global.Log.LogInfo(text);

            Notification.Popup("Game Speed", text);
        }

        if (Input.GetKeyDown(KeyCode.F12))
        {
            var username = Environment.UserName;
            var timeFormat = DateTime.Now.ToString("yyyyMMdd_HHmmssff");
            var location = string.Format("C:\\Users\\{0}\\Pictures\\tsk_{1}.png", username, timeFormat);
            ScreenCapture.CaptureScreenshot(location);

            Notification.SsPopup(location);
        }

        LastSkipExecuteTime += Time.deltaTime;
        if (LastSkipExecuteTime >= CtrlWaitTime && Input.GetKey(KeyCode.LeftControl) || LastSkipExecuteTime >= CtrlWaitTime && Input.GetKey(KeyCode.RightControl))
        {
            LastSkipExecuteTime = 0.0f;
            AdvEngine advEngine = FindObjectOfType<AdvEngine>() as AdvEngine;
            if (advEngine != null)
            {
                advEngine.page.EndPage();
            }
        }

        LastGSExecuteTime += Time.deltaTime;
        if (IsGameSpeedChanged && LastGSExecuteTime >= WaitTime && Time.timeScale != CurrentGameSpeed)
        {
            LastGSExecuteTime = 0.0f;
            Time.timeScale = CurrentGameSpeed;
            Plugin.Global.Log.LogInfo("Game speed changed. Reset to: " + CurrentGameSpeed + "x");
        }

        LastFPSExecuteTime += Time.deltaTime;
        if (TSKConfig.FPS > 60 && LastFPSExecuteTime >= WaitTime && Application.targetFrameRate < TSKConfig.FPS)
        {
            LastFPSExecuteTime = 0.0f;
            Application.targetFrameRate = TSKConfig.FPS;
            Plugin.Global.Log.LogInfo("FPS changed. Reset to: " + TSKConfig.FPS);
        }
    }
}