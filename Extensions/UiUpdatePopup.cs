using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UObject = UnityEngine.Object;

namespace TSKHook;

// Owned by the plugin; no game prefab, input handlers or extra font assets.
internal static class UiUpdatePopup
{
    private static GameObject root;
    private static TextMeshProUGUI label;
    private static RectTransform fill;
    private static string message;
    private static float progress;
    private static DateTime expires;
    private static bool failed;
    private static string appliedMessage;
    private static ChineseScript appliedScript;
    private static float appliedProgress = float.NaN;

    internal static void Show(string text, float fraction = -1, bool complete = false)
    {
        message = text;
        progress = fraction;
        expires = complete ? DateTime.UtcNow.AddSeconds(6) : DateTime.MaxValue;
        Update();
    }

    internal static void Update()
    {
        if (message == null || failed) return;
        try
        {
            if (DateTime.UtcNow >= expires)
            {
                if (root != null) root.SetActive(false);
                message = null;
                return;
            }
            // The existing Chinese TMP bundle is reused; retry naturally once the font is ready.
            if (Patch.TMPTranslateFont == null) return;
            if (root == null) Create();
            if (!root.activeSelf) root.SetActive(true);
            var script = TSKConfig.Settings.Script.Value;
            if (message != appliedMessage || script != appliedScript)
            {
                label.text = ChineseTextConverter.Convert(message,script);
                appliedMessage = message; appliedScript = script;
            }
            if (progress != appliedProgress)
            {
                fill.gameObject.SetActive(progress >= 0);
                fill.anchorMax = new Vector2(Mathf.Clamp01(progress), 1);
                appliedProgress = progress;
            }
        }
        catch (Exception e)
        {
            failed = true;
            if (root != null) UObject.Destroy(root);
            Plugin.Global.Log.LogWarning("[Updates] Popup unavailable: " + e.Message);
        }
    }

    private static void Create()
    {
        appliedMessage = null; appliedProgress = float.NaN;
        root = new GameObject("TSKHook.UpdateNotice");
        UObject.DontDestroyOnLoad(root);
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32760;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = 1;
        // No GraphicRaycaster: the popup never intercepts clicks or hover.
        var panel = Rect("Panel", root.transform);
        panel.anchorMin = panel.anchorMax = new Vector2(1, 0);
        panel.pivot = new Vector2(1, 0);
        panel.anchoredPosition = new Vector2(-18, 18);
        panel.sizeDelta = new Vector2(320, 90);
        var background = panel.gameObject.AddComponent<Image>();
        background.color = new Color(0.06f, 0.09f, 0.16f, 0.94f);
        background.raycastTarget = false;
        var text = Rect("Message", panel);
        text.anchorMin = Vector2.zero; text.anchorMax = Vector2.one;
        text.offsetMin = new Vector2(14, 18); text.offsetMax = new Vector2(-14, -10);
        label = text.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = Patch.TMPTranslateFont;
        label.fontSize = 16;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.richText = false;
        label.enableWordWrapping = true;
        label.raycastTarget = false;
        var track = Rect("Progress", panel);
        track.anchorMin = Vector2.zero; track.anchorMax = new Vector2(1, 0);
        track.offsetMin = new Vector2(14, 10); track.offsetMax = new Vector2(-14, 13);
        var trackImage = track.gameObject.AddComponent<Image>();
        trackImage.color = new Color(0.2f, 0.25f, 0.32f, 1);
        trackImage.raycastTarget = false;
        fill = Rect("Fill", track);
        fill.anchorMin = Vector2.zero; fill.anchorMax = Vector2.one;
        fill.offsetMin = fill.offsetMax = Vector2.zero;
        var bar = fill.gameObject.AddComponent<Image>();
        bar.color = new Color(0.35f, 0.75f, 0.98f, 1);
        bar.raycastTarget = false;
    }
    private static RectTransform Rect(string name, Transform parent)
    {
        var item = new GameObject(name, new Il2CppSystem.Type[] { Il2CppInterop.Runtime.Il2CppType.Of<RectTransform>() });
        var rect = item.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }
    internal static void Shutdown()
    {
        if (root != null) UObject.Destroy(root);
        root = null; label = null; fill = null; message = null;
    }
}
