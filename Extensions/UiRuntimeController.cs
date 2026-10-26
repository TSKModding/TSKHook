using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace TSKHook;

internal static class UiRuntimeController
{
    private static readonly CancellationTokenSource Lifetime = new();
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(45) };
    private static readonly ConcurrentQueue<Action> MainThread = new();
    private static readonly string Root = Path.Combine(Paths.PluginPath, "TSKHook");
    private static readonly TranslationPackageStore Packages = new(Path.Combine(Root, "packages"));
    private static readonly ImagePackageStore Images = new(Path.Combine(Root, "image-packages"));
    private static readonly DateTime startupUpdateAt = DateTime.UtcNow.AddSeconds(15);
    private static bool startupUpdatePending = true;
    private static DateTime nextPreview;
    private static DateTime previewStamp;
    private static int updateRunning;
    private static int stopped;
    private static int width, height, fps;
    private static bool preview;
    private static object manager;
    private static PropertyInfo managerVisible;
    private static bool managerSearched;
    private static bool runtimeReady;
    private static int textRequest, imageRequest;
    private static PreparedText pendingText;
    private static PreparedImages pendingImages;
    private static Action pendingProgress;
    private static readonly SemaphoreSlim PreparationGate = new(1,1);
    private sealed record PreparedText(int Request, string Path, UiTranslationIndex Index);
    private sealed record PreparedImages(int Request, PreparedImagePackage Package);

    internal static void Initialize()
    {
        width = TSKConfig.width; height = TSKConfig.height; fps = TSKConfig.FPS;
        preview = TSKConfig.Settings.LocalPreview.Value;
        Client.DefaultRequestHeaders.UserAgent.ParseAdd("TSKHook/" + MyPluginInfo.PLUGIN_VERSION);
        UiTextCaptureService.Initialize();
        UiTranslationService.Initialize();
        try { Images.SeedLocalAsync(Path.Combine(Root, "ui_images")).GetAwaiter().GetResult(); }
        catch (Exception e) { Plugin.Global.Log.LogWarning("[UI Images] Local seed skipped: " + e.Message); }
        LoadActiveImages();
        try { Packages.SeedLocalAsync(Path.Combine(Root, "ui_translations.jsonl")).GetAwaiter().GetResult(); }
        catch (Exception e) { Plugin.Global.Log.LogWarning("[UI Translation] Local package seed skipped: " + e.Message); }
        LoadActiveTranslations();
        Patch.EnsureTranslationFontLoaded();
        runtimeReady = true;
    }

    // All configuration application and Unity-facing operations happen here, on the game thread.
    internal static bool Update()
    {
        if (Volatile.Read(ref stopped) != 0) return false;
        var deadline = System.Diagnostics.Stopwatch.GetTimestamp() + System.Diagnostics.Stopwatch.Frequency/2000;
        for (var i = 0; i < 8 && System.Diagnostics.Stopwatch.GetTimestamp() < deadline && MainThread.TryDequeue(out var callback); i++) callback();
        Interlocked.Exchange(ref pendingProgress,null)?.Invoke();
        // Preparation is complete before publication; do not change resources during a battle.
        if (!UiRuntimeActivity.InBattle)
        {
            var text = Interlocked.Exchange(ref pendingText,null);
            if (text != null && text.Request == Volatile.Read(ref textRequest)) UiTranslationService.Install(text.Index,text.Path);
            var image = Interlocked.Exchange(ref pendingImages,null);
            if (image != null && image.Request == Volatile.Read(ref imageRequest)) UiImageTranslationService.Install(image.Package);
        }
        UiUpdatePopup.Update();
        if (TSKConfig.ConsumeChange())
        {
            try
            {
                if (width != TSKConfig.width || height != TSKConfig.height || fps != TSKConfig.FPS)
                {
                    Window.Init();
                    width = TSKConfig.width; height = TSKConfig.height; fps = TSKConfig.FPS;
                }
                UiTextCaptureService.Configure();
                if (preview != TSKConfig.Settings.LocalPreview.Value)
                {
                    preview = TSKConfig.Settings.LocalPreview.Value;
                    LoadActiveTranslations();
                }
            }
            catch (Exception e) { Report("配置应用失败：" + e.Message); }
        }
        var action = TSKConfig.Settings.Action.Value;
        // ConfigurationManager owns F1 when loaded; otherwise keep the original reload shortcut.
        if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.F1) && !HasConfigurationManager)
            action = MaintenanceAction.ReloadConfiguration;
        if (action != MaintenanceAction.None)
        {
            TSKConfig.Settings.Action.Value = MaintenanceAction.None;
            try
            {
                switch (action)
                {
                    case MaintenanceAction.ReloadConfiguration:
                        TSKConfig.Read();
                        Window.Init();
                        Patch.EnsureTranslationFontLoaded();
                        LoadActiveTranslations();
                        Report("配置已重新读取并应用。");
                        break;
                    case MaintenanceAction.ReloadTranslations: LoadActiveTranslations(); LoadActiveImages(); break;
                    case MaintenanceAction.CheckForUpdates: StartUpdate(false); break;
                    case MaintenanceAction.RollbackTranslations: StartUpdate(true); break;
                    case MaintenanceAction.RollbackImages: StartUpdate(true, true); break;
                }
            }
            catch (Exception e) { Report("操作失败：" + e.Message); }
        }
        var now = DateTime.UtcNow;
        if (startupUpdatePending && now >= startupUpdateAt)
        {
            startupUpdatePending = false;
            if (TSKConfig.Settings.AutoUpdate.Value && !TSKConfig.Settings.LocalPreview.Value) StartUpdate(false);
        }
        if (preview && now >= nextPreview)
        {
            nextPreview = now.AddSeconds(2);
            var stamp = File.GetLastWriteTimeUtc(Path.Combine(Root, "ui_preview.jsonl"));
            if (stamp != previewStamp) { previewStamp = stamp; LoadActiveTranslations(); }
        }
        UiImageTranslationService.Update();
        UiFontFallbackPatch.Update();
        return IsManagerVisible();
    }

    internal static void LoadActiveTranslations()
    {
        var request = Interlocked.Increment(ref textRequest);
        var usePreview = TSKConfig.Settings.LocalPreview.Value;
        void Prepare()
        {
            try
            {
                string path;
                if (usePreview) path = Path.Combine(Root,"ui_preview.jsonl");
                else
                {
                    var state = Packages.ReadState();
                    if (state.Current != null) TranslationPackageStore.Verify(Packages.PackagePath(state.Current),state.Current);
                    path = state.Current != null ? Packages.PackagePath(state.Current) : Path.Combine(Root,"ui_translations.jsonl");
                }
                var candidate = new PreparedText(request,path,RuntimeAsset.Load(path));
                if (Lifetime.IsCancellationRequested || request != Volatile.Read(ref textRequest)) return;
                if (!runtimeReady) UiTranslationService.Install(candidate.Index,candidate.Path);
                else MainThread.Enqueue(() => { if (!Lifetime.IsCancellationRequested && request == Volatile.Read(ref textRequest)) pendingText = candidate; });
            }
            catch (Exception e) { Plugin.Global.Log.LogWarning("[UI Translation] Active package unavailable; previous index retained: " + e.Message); }
        }
        if (runtimeReady) RunPreparation(() => { if (request == Volatile.Read(ref textRequest)) Prepare(); }); else Prepare();
    }

    private static void LoadActiveImages()
    {
        var request = Interlocked.Increment(ref imageRequest);
        void Prepare()
        {
            try
            {
                var state = Images.ReadState();
                if (state.Current != null) ImagePackageStore.VerifyDirectory(Images.PackagePath(state.Current),state.Current);
                var path = state.Current != null ? Images.PackagePath(state.Current) : Path.Combine(Root,"ui_images");
                var candidate = new PreparedImages(request,PreparedImagePackage.Load(path));
                if (Lifetime.IsCancellationRequested || request != Volatile.Read(ref imageRequest)) return;
                if (!runtimeReady) UiImageTranslationService.Install(candidate.Package);
                else MainThread.Enqueue(() => { if (!Lifetime.IsCancellationRequested && request == Volatile.Read(ref imageRequest)) pendingImages = candidate; });
            }
            catch (Exception e) { Plugin.Global.Log.LogWarning("[UI Images] Active package unavailable; previous images retained: " + e.Message); }
        }
        if (runtimeReady) RunPreparation(() => { if (request == Volatile.Read(ref imageRequest)) Prepare(); }); else Prepare();
    }

    private static void RunPreparation(Action prepare) => _ = Task.Run(async () =>
    {
        try
        {
            await PreparationGate.WaitAsync(Lifetime.Token);
            try { if (!Lifetime.IsCancellationRequested) prepare(); }
            finally { PreparationGate.Release(); }
        }
        catch (OperationCanceledException) when (Lifetime.IsCancellationRequested) { }
    });

    private static void StartUpdate(bool rollback, bool imagesOnly = false)
    {
        if (TSKConfig.Settings.LocalPreview.Value) { Report("本地预览模式已暂停公共翻译包更新；请先关闭预览模式。"); return; }
        if (Interlocked.CompareExchange(ref updateRunning, 1, 0) != 0) return;
        var url = TSKConfig.Settings.ManifestUrl.Value.Trim();
        var imageUrl = TSKConfig.Settings.ImageManifestUrl.Value.Trim();
        Interlocked.Exchange(ref pendingProgress,null);
        UiUpdatePopup.Show(rollback ? (imagesOnly ? "正在回退图片资源…" : "正在回退文字资源…") : "正在检查翻译资源更新…");
        _ = Task.Run(async () =>
        {
            var results = new System.Collections.Generic.List<string>();
            async Task RunChannel(string channel, Func<Task<bool>> update, Action reload)
            {
                try
                {
                    var changed = await update();
                    results.Add(channel + (changed ? "已更新" : "已是当前版本"));
                    if (changed && !Lifetime.IsCancellationRequested) MainThread.Enqueue(reload);
                }
                catch (OperationCanceledException) when (Lifetime.IsCancellationRequested) { throw; }
                catch (Exception e)
                {
                    results.Add(channel + "更新失败，保留本地资源");
                    if (!Lifetime.IsCancellationRequested) MainThread.Enqueue(() => Report(channel + "更新未完成：" + e.Message));
                }
            }
            Action<ResourceUpdateProgress> Progress(string channel) => value =>
            {
                if (Lifetime.IsCancellationRequested) return;
                Interlocked.Exchange(ref pendingProgress,() =>
                {
                    var text = value.Stage switch
                    {
                        "Downloading" => $"正在下载{channel} {value.Version}\n{value.Bytes / 1024:N0} / {value.Total / 1024:N0} KB  ({value.Bytes * 100 / Math.Max(1, value.Total)}%)",
                        "Verifying" => $"正在校验{channel} {value.Version}…",
                        "Installing" => $"正在应用{channel} {value.Version}…",
                        _ => $"正在检查{channel}更新…"
                    };
                    UiUpdatePopup.Show(text, value.Stage == "Downloading" ? (float)value.Bytes / Math.Max(1, value.Total) : -1);
                });
            };
            try
            {
                if (rollback)
                {
                    if (imagesOnly) await Images.RollbackAsync(Lifetime.Token);
                    else await Packages.RollbackAsync(Lifetime.Token);
                    results.Add(imagesOnly ? "图片资源已回退" : "文字资源已回退");
                    if (!Lifetime.IsCancellationRequested) MainThread.Enqueue(imagesOnly ? LoadActiveImages : LoadActiveTranslations);
                }
                else
                {
                    await RunChannel("文字资源", () => Packages.UpdateAsync(Client, url, MyPluginInfo.PLUGIN_VERSION, Lifetime.Token, Progress("文字资源")), LoadActiveTranslations);
                    await RunChannel("图片资源", () => Images.UpdateAsync(Client, imageUrl, MyPluginInfo.PLUGIN_VERSION, Lifetime.Token, Progress("图片资源")), LoadActiveImages);
                }
                if (!Lifetime.IsCancellationRequested) MainThread.Enqueue(() =>
                {
                    Interlocked.Exchange(ref pendingProgress,null);
                    var message = string.Join("\n", results);
                    Report(message);
                    UiUpdatePopup.Show(message, complete: true);
                });
            }
            catch (OperationCanceledException) when (Lifetime.IsCancellationRequested) { }
            catch (Exception e)
            {
                if (!Lifetime.IsCancellationRequested) MainThread.Enqueue(() =>
                {
                    Interlocked.Exchange(ref pendingProgress,null);
                    Report("资源切换未完成：" + e.Message);
                    UiUpdatePopup.Show("资源切换未完成，保留现有资源", complete: true);
                });
            }
            finally
            {
                if (!Lifetime.IsCancellationRequested) MainThread.Enqueue(() => Interlocked.Exchange(ref updateRunning, 0));
                else Interlocked.Exchange(ref updateRunning, 0);
            }
        });
    }

    private static bool HasConfigurationManager
    {
        get
        {
            if (!managerSearched)
            {
                managerSearched = true;
                foreach (var entry in IL2CPPChainloader.Instance.Plugins.Values)
                    if (entry.Metadata.GUID == "com.bepis.bepinex.configurationmanager")
                    {
                        manager = entry.Instance;
                        managerVisible = manager?.GetType().GetProperty("DisplayingWindow");
                        break;
                    }
                if (manager == null) Plugin.Global.Log.LogInfo("[Config] ConfigurationManager not loaded; F1 reloads configuration.");
            }
            return manager != null;
        }
    }

    private static bool IsManagerVisible() => HasConfigurationManager && managerVisible?.GetValue(manager) is true;

    private static void Report(string message)
    {
        Plugin.Global.Log.LogInfo("[UI Translation] " + message);
        // Upstream Popup concatenates text into a PowerShell script. Never pass remote/file errors to it.
        TSKConfig.Settings.LastResult.Value = DateTime.Now.ToString("HH:mm:ss") + " " + message;
    }

    internal static void Shutdown()
    {
        if (Interlocked.Exchange(ref stopped, 1) != 0) return;
        Lifetime.Cancel();
        Interlocked.Exchange(ref pendingText,null);
        Interlocked.Exchange(ref pendingImages,null);
        Interlocked.Exchange(ref pendingProgress,null);
        UiTextCaptureService.Shutdown();
        UiImageTranslationService.Shutdown();
        UiUpdatePopup.Shutdown();
        UiTranslationService.Shutdown();
    }
}
