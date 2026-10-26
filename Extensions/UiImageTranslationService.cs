using System.Collections;
using System.Diagnostics;
using Il2CppInterop.Runtime;
using Spine;
using Spine.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UObject = UnityEngine.Object;
using NVector2 = System.Numerics.Vector2;

namespace TSKHook;

// Event notifications only enqueue; CPU preparation is asynchronous; Unity mutations stay on the game thread.
internal static class UiImageTranslationService
{
    private static PreparedImagePackage package;
    private static Dictionary<string, UiSpriteRule> spriteRules = new(StringComparer.Ordinal);
    private static Dictionary<string, UiSpineRule> spineRules = new(StringComparer.Ordinal);
    private static readonly Dictionary<int, SpriteAsset> sprites = new();
    private static readonly Dictionary<(int, string), AtlasAsset> atlases = new();
    private static readonly Dictionary<string, Texture2D> files = new(StringComparer.Ordinal);
    private static readonly Dictionary<int, ImageBinding> images = new();
    private static readonly Dictionary<int, SpineBinding> graphics = new();
    private static readonly BoundedWorkQueue<int, Component> Work = new(4096);
    private static readonly BoundedWorkQueue<int, ImageWatch> Watching = new(4096);
    private static readonly HashSet<string> reported = new(StringComparer.Ordinal);
    private static CancellationTokenSource lifetime = new();
    private static IEnumerator discovery;
    private static bool discover = true, enabled, mutating;
    private static long nextDiscovery, nextCleanup;
    private static int workers;
    private static bool Requested => package != null && TSKConfig.TranslationEnabled && TSKConfig.UiTranslationEnabled && TSKConfig.Settings.ImageTranslation.Value;

    internal static bool Reload(string directory)
    {
        try { Install(PreparedImagePackage.Load(directory)); return true; }
        catch (Exception e) { Warn("manifest", "Package unavailable; previous images retained: " + e.Message); return false; }
    }
    internal static void Install(PreparedImagePackage candidate)
    {
        var sameRules = candidate.Manifest.HasSameRules(package?.Manifest);
        if (!sameRules) Shutdown();
        package = candidate;
        spriteRules = candidate.Manifest.Sprites.ToDictionary(x => x.Sprite, StringComparer.Ordinal);
        spineRules = candidate.Manifest.Spine.ToDictionary(x => x.Asset, StringComparer.Ordinal);
        reported.Clear();
        RequestDiscovery();
        Plugin.Global.Log.LogInfo($"[UI Images] Loaded {candidate.Manifest.Version}: {spriteRules.Count} sprites, {spineRules.Count} Spine cards; event queue active.");
    }
    internal static void Notify(Component component)
    {
        if (mutating || !Requested || component == null) return;
        if (!Work.Enqueue(component.GetInstanceID(),component)) discover = true;
        var image = component.TryCast<Image>();
        if (image != null && !Watching.Enqueue(component.GetInstanceID(),new ImageWatch(image))) discover = true;
    }
    internal static void RequestDiscovery() => discover = true;

    internal static void Update()
    {
        try
        {
            var requested = Requested;
            if (!requested)
            {
                if (enabled) { Restore(); CancelWork(); }
                enabled = false;
                UiTextureReadback.Update(false);
                return;
            }
            UiTextureReadback.Update(!UiRuntimeActivity.InBattle);
            if (!enabled) { enabled = true; RequestDiscovery(); }
            var deadline = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency*.0008);
            var count = Math.Min(64,Work.Count);
            mutating = true;
            // Snapshot the count, not the queue: unfinished objects are visited at most once per frame.
            for (var i = 0; i < count && Stopwatch.GetTimestamp() < deadline; i++)
            {
                if (!Work.TryDequeue(out var component)) break;
                if (component == null) continue;
                try
                {
                    var image = component.TryCast<Image>();
                    var complete = image != null ? Process(image) : Process(component.TryCast<SkeletonGraphic>());
                    if (!complete) Work.Enqueue(component.GetInstanceID(),component);
                }
                catch (Exception e) { Warn("object:" + component.GetInstanceID(),e.Message); }
            }
            mutating = false;
            if (UiRuntimeActivity.InBattle) return;
            PollImages(deadline);
            var now = Stopwatch.GetTimestamp();
            if (now >= nextCleanup)
            {
                mutating = true;
                try { Cleanup(); } finally { mutating = false; }
                nextCleanup = now + 10*Stopwatch.Frequency;
            }
            if (discovery == null && (discover || now >= nextDiscovery))
            {
                discovery = Discover(); discover = false;
                nextDiscovery = now + 30*Stopwatch.Frequency;
            }
            if (discovery != null && Stopwatch.GetTimestamp() < deadline)
            {
                for (var i = 0; i < 32 && Stopwatch.GetTimestamp() < deadline; i++)
                    if (!discovery.MoveNext()) { (discovery as IDisposable)?.Dispose(); discovery = null; break; }
            }
        }
        catch (Exception e) { Warn("queue",e.ToString()); (discovery as IDisposable)?.Dispose(); discovery = null; }
        finally { mutating = false; }
    }

    private static void PollImages(long frameDeadline)
    {
        // Only already discovered components, never a global search. Inactive objects re-register on OnEnable.
        var now = Stopwatch.GetTimestamp();
        var deadline = Math.Min(frameDeadline,now+Stopwatch.Frequency/5000);
        var count = Math.Min(32,Watching.Count);
        for (var i = 0; i < count && Stopwatch.GetTimestamp() < deadline; i++)
        {
            if (!Watching.TryDequeue(out var watch)) break;
            var image = watch.Image;
            if (image == null || !image.enabled || !image.gameObject.activeInHierarchy) continue;
            if (now >= watch.NextCheck)
            {
                var source = image.sprite;
                var overlay = image.overrideSprite;
                if (!watch.Initialized || source != watch.Source || overlay != watch.Overlay)
                {
                    if (!Work.Enqueue(image.GetInstanceID(),image)) discover = true;
                    watch.Source = source; watch.Overlay = overlay; watch.Initialized = true;
                }
                watch.NextCheck = now+Stopwatch.Frequency/10;
            }
            Watching.Enqueue(image.GetInstanceID(),watch);
        }
    }

    // Incremental scene traversal replaces even the fallback global scan. Paused throughout battle.
    private static IEnumerator Discover()
    {
        var stack = new Stack<(Transform Node, int Child)>();
        for (var sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
        {
            var scene = SceneManager.GetSceneAt(sceneIndex);
            if (!scene.IsValid() || !scene.isLoaded) continue;
            // The parameterless overload in this game's interop DLL is an unstripping placeholder that throws.
            var roots = new Il2CppSystem.Collections.Generic.List<GameObject>();
            scene.GetRootGameObjects(roots);
            for (var rootIndex = 0; rootIndex < roots.Count; rootIndex++)
            {
                var root = roots[rootIndex];
                if (root == null) continue;
                stack.Push((root.transform,-1));
                while (stack.Count != 0)
                {
                    var (node,child) = stack.Pop();
                    if (node == null) continue;
                    if (child == -1)
                    {
                        if (!node.gameObject.activeInHierarchy) continue;
                        var image = node.GetComponent(Il2CppType.Of<Image>())?.TryCast<Image>(); if (image != null) Notify(image);
                        var graphic = node.GetComponent(Il2CppType.Of<SkeletonGraphic>())?.TryCast<SkeletonGraphic>(); if (graphic != null) Notify(graphic);
                        child = 0;
                        yield return null;
                    }
                    if (node != null && child < node.childCount)
                    { stack.Push((node,child+1)); stack.Push((node.GetChild(child),-1)); }
                }
                yield return null;
            }
        }
    }

    private static bool Process(Image image)
    {
        var key = image.GetInstanceID();
        if (images.TryGetValue(key,out var binding))
        {
            if (image.sprite == binding.Base && image.overrideSprite == binding.Replacement) return true;
            if (image.overrideSprite == binding.Replacement) image.overrideSprite = null;
            images.Remove(key);
        }
        if (!image.enabled || !image.gameObject.activeInHierarchy || !image.gameObject.scene.IsValid()) return true;
        var source = image.overrideSprite ?? image.sprite;
        if (source == null || source.texture == null || !spriteRules.TryGetValue(source.name,out var rule)) return true;
        if (Mathf.RoundToInt(source.rect.width) != rule.Width || Mathf.RoundToInt(source.rect.height) != rule.Height) return true;
        var id = source.GetInstanceID();
        if (!sprites.TryGetValue(id,out var asset) || asset.Source != source)
        {
            if (UiRuntimeActivity.InBattle || Volatile.Read(ref workers) >= 2) return false;
            var pixels = UiTextureReadback.Get(source.texture);
            var vertices = source.vertices.Select(x => new NVector2(x.x,x.y)).ToArray();
            var uv = source.uv.Select(x => new NVector2(x.x,x.y)).ToArray();
            var triangles = source.triangles.ToArray();
            var pivot = new NVector2(source.pivot.x,source.pivot.y); var scale = source.pixelsPerUnit;
            var token = lifetime.Token;
            Interlocked.Increment(ref workers);
            var ready = Task.Run(async () =>
            {
                try
                {
                    var atlas = await pixels.ConfigureAwait(false); token.ThrowIfCancellationRequested();
                    var actual = atlas.Sprite(rule.Width,rule.Height,vertices,uv,triangles,scale,pivot).CanonicalHash();
                    token.ThrowIfCancellationRequested();
                    if (actual != rule.SourceRgbaSha256 && atlas.Sprite(rule.Width,rule.Height,vertices,uv,triangles,scale,pivot,true).CanonicalHash() != rule.SourceRgbaSha256)
                        throw new InvalidDataException("Source sprite pixels changed; kept original artwork.");
                    return true;
                }
                finally { Interlocked.Decrement(ref workers); }
            });
            sprites[id] = asset = new SpriteAsset(source,ready);
        }
        asset.LastUse = Stopwatch.GetTimestamp();
        if (asset.Rejected) return true;
        if (asset.Replacement == null)
        {
            if (!asset.Ready.IsCompleted || UiRuntimeActivity.InBattle) return false;
            if (asset.Ready.IsCanceled || asset.Ready.IsFaulted)
            { asset.Rejected = true; Warn(rule.Sprite,asset.Ready.Exception?.GetBaseException().Message ?? "Image preparation canceled."); return true; }
            var texture = GetFile(rule.File);
            texture.filterMode = source.texture.filterMode;
            var pivot = new Vector2(source.pivot.x/rule.Width,source.pivot.y/rule.Height);
            asset.Replacement = Sprite.Create(texture,new Rect(0,0,rule.Width,rule.Height),pivot,source.pixelsPerUnit,0,SpriteMeshType.FullRect,source.border);
            asset.Replacement.name = "TSKHook.zh." + source.name;
            asset.Replacement.hideFlags = HideFlags.DontUnloadUnusedAsset;
        }
        images[key] = new(image,image.sprite,source == image.sprite ? null : source,asset.Replacement);
        image.overrideSprite = asset.Replacement;
        return true;
    }

    private static bool Process(SkeletonGraphic graphic)
    {
        if (graphic == null) return true;
        var key = graphic.GetInstanceID();
        if (graphics.TryGetValue(key,out var binding))
        {
            if (graphic.SkeletonDataAsset == binding.Asset && Owns(binding)) return true;
            Restore(binding); graphics.Remove(key);
        }
        if (!graphic.enabled || !graphic.gameObject.activeInHierarchy || !graphic.gameObject.scene.IsValid() ||
            graphic.SkeletonDataAsset == null || !spineRules.TryGetValue(graphic.SkeletonDataAsset.name,out var rule)) return true;
        foreach (var atlasBase in graphic.SkeletonDataAsset.atlasAssets)
        {
            var atlas = atlasBase == null ? null : atlasBase.TryCast<SpineAtlasAsset>();
            if (atlas == null) continue;
            foreach (var material in atlas.materials)
            {
                var source = material?.mainTexture;
                if (source == null || source.name != rule.Texture) continue;
                var multiple = graphic.allowMultipleCanvasRenderers;
                if (!multiple && graphic.baseTexture != source) continue;
                var hadMapping = graphic.CustomTextureOverride.TryGetValue(source,out var mapping);
                if (multiple && hadMapping && mapping == null) { graphic.CustomTextureOverride.Remove(source); hadMapping = false; }
                if (multiple && hadMapping || !multiple && graphic.OverrideTexture != null) continue;
                var id = (source.GetInstanceID(),rule.Asset);
                if (!atlases.TryGetValue(id,out var asset) || asset.Source != source || !asset.Rejected && asset.Replacement == null && asset.Ready == null)
                {
                    if (UiRuntimeActivity.InBattle || Volatile.Read(ref workers) >= 2) return false;
                    var shader = graphic.material;
                    if (shader == null || !shader.HasProperty("_StraightAlphaInput") || shader.GetFloat("_StraightAlphaInput") < .5f)
                    { Warn(rule.Asset,"Unsupported Spine alpha mode."); return true; }
                    var regions = new List<(PixelRegion Geometry, UiSpineRegionRule Rule)>();
                    foreach (var item in rule.Regions)
                    {
                        var r = atlas.GetAtlas().FindRegion(item.Name);
                        if (r == null || r.page?.rendererObject?.TryCast<Material>()?.mainTexture != source ||
                            r.width != item.Width || r.height != item.Height || r.degrees != item.Degrees ||
                            r.originalWidth != item.OriginalWidth || r.originalHeight != item.OriginalHeight ||
                            Math.Abs(r.offsetX-item.OffsetX) > .01f || Math.Abs(r.offsetY-item.OffsetY) > .01f ||
                            r.x < 0 || r.y < 0 || r.x+(r.degrees == 90 ? r.height : r.width) > source.width ||
                            r.y+(r.degrees == 90 ? r.width : r.height) > source.height)
                        { Warn(rule.Asset,"Source title geometry changed; kept original artwork."); return true; }
                        regions.Add((new(r.x,r.y,r.width,r.height,r.degrees),item));
                    }
                    var pixels = UiTextureReadback.Get(source);
                    var prepared = package; var token = lifetime.Token;
                    Interlocked.Increment(ref workers);
                    var ready = Task.Run(async () =>
                    {
                        try
                        {
                            var original = await pixels.ConfigureAwait(false); token.ThrowIfCancellationRequested();
                            // GPU backends can expose opposite row origins. Accept an orientation only after every region's hash verifies.
                            var first = regions[0];
                            var flip = original.Region(first.Geometry).CanonicalHash() != first.Rule.SourceRgbaSha256;
                            foreach (var item in regions)
                                if (original.Region(item.Geometry,flip).CanonicalHash() != item.Rule.SourceRgbaSha256)
                                    throw new InvalidDataException("Source title pixels changed; kept original artwork: " + item.Rule.Name);
                            var result = original.Copy(flip);
                            foreach (var item in regions) { token.ThrowIfCancellationRequested(); result.Patch(item.Geometry,prepared.Pixels[item.Rule.File]); }
                            return result;
                        }
                        finally { Interlocked.Decrement(ref workers); }
                    });
                    atlases[id] = asset = new AtlasAsset(source,ready);
                }
                asset.LastUse = Stopwatch.GetTimestamp();
                if (asset.Rejected) return true;
                if (asset.Replacement == null)
                {
                    if (!asset.Ready.IsCompleted || UiRuntimeActivity.InBattle) return false;
                    if (asset.Ready.IsCanceled || asset.Ready.IsFaulted)
                    { asset.Rejected = true; Warn(rule.Asset,asset.Ready.Exception?.GetBaseException().Message ?? "Atlas preparation canceled."); return true; }
                    asset.Replacement = Upload(asset.Ready.Result,"TSKHook.zh."+source.name);
                    asset.Replacement.filterMode = source.filterMode; asset.Replacement.wrapMode = source.wrapMode;
                    asset.Ready = null;
                }
                graphics[key] = new(graphic,graphic.SkeletonDataAsset,source,asset.Replacement,multiple);
                if (multiple) graphic.CustomTextureOverride[source] = asset.Replacement;
                else graphic.OverrideTexture = asset.Replacement;
                graphic.UpdateMesh(true);
                return true;
            }
        }
        return true;
    }

    private static Texture2D GetFile(string file)
    {
        if (!files.TryGetValue(file,out var texture) || texture == null)
            files[file] = texture = Upload(package.Pixels[file],"TSKHook.zh."+Path.GetFileNameWithoutExtension(file));
        return texture;
    }
    private static unsafe Texture2D Upload(RgbaPixels pixels,string name)
    {
        var texture = new Texture2D(pixels.Width,pixels.Height,TextureFormat.RGBA32,false,false);
        try
        {
            fixed (byte* pointer = pixels.Bytes) texture.LoadRawTextureData((IntPtr)pointer,pixels.Bytes.Length);
            texture.Apply(false,true);
            texture.name = name; texture.wrapMode = TextureWrapMode.Clamp;
            texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return texture;
        }
        catch { UObject.Destroy(texture); throw; }
    }
    private static bool Owns(SpineBinding item) => item.Graphic != null && item.Source != null &&
        (item.Multiple ? item.Graphic.CustomTextureOverride.TryGetValue(item.Source,out var mapped) && mapped == item.Replacement : item.Graphic.OverrideTexture == item.Replacement);
    private static void Restore(SpineBinding item)
    {
        if (!Owns(item)) return;
        if (item.Multiple) item.Graphic.CustomTextureOverride.Remove(item.Source);
        else item.Graphic.OverrideTexture = null;
        item.Graphic.UpdateMesh(true);
    }
    private static void Restore()
    {
        var previous = mutating; mutating = true;
        try
        {
            foreach (var item in images.Values)
                if (item.Image != null && item.Image.overrideSprite == item.Replacement)
                    item.Image.overrideSprite = item.Image.sprite == item.Base ? item.OriginalOverride : null;
            images.Clear();
            foreach (var item in graphics.Values) Restore(item);
            graphics.Clear();
        }
        finally { mutating = previous; }
    }
    private static void Cleanup()
    {
        var cutoff = Stopwatch.GetTimestamp()-30*Stopwatch.Frequency;
        foreach (var pair in images.ToArray())
        {
            var item = pair.Value;
            if (item.Image == null) { images.Remove(pair.Key); continue; }
            if (!item.Image.enabled || !item.Image.gameObject.activeInHierarchy)
            {
                if (item.Image.overrideSprite == item.Replacement)
                    item.Image.overrideSprite = item.Image.sprite == item.Base ? item.OriginalOverride : null;
                images.Remove(pair.Key);
            }
        }
        foreach (var pair in graphics.ToArray())
            if (pair.Value.Graphic == null || !pair.Value.Graphic.enabled || !pair.Value.Graphic.gameObject.activeInHierarchy)
            { Restore(pair.Value); graphics.Remove(pair.Key); }
        foreach (var pair in sprites.ToArray())
            if ((pair.Value.Source == null || pair.Value.LastUse < cutoff) && !images.Values.Any(x => x.Replacement == pair.Value.Replacement))
            { if (pair.Value.Replacement != null) UObject.Destroy(pair.Value.Replacement); sprites.Remove(pair.Key); }
        foreach (var pair in atlases.ToArray())
            if ((pair.Value.Source == null || pair.Value.LastUse < cutoff) && !graphics.Values.Any(x => x.Replacement == pair.Value.Replacement))
            { if (pair.Value.Replacement != null) UObject.Destroy(pair.Value.Replacement); atlases.Remove(pair.Key); }
    }
    private static void CancelWork()
    {
        lifetime.Cancel(); lifetime.Dispose(); lifetime = new();
        Work.Clear(); Watching.Clear(); (discovery as IDisposable)?.Dispose(); discovery = null;
        UiTextureReadback.Clear();
        foreach (var key in sprites.Where(x => x.Value.Replacement == null).Select(x => x.Key).ToArray()) sprites.Remove(key);
        foreach (var key in atlases.Where(x => x.Value.Replacement == null).Select(x => x.Key).ToArray()) atlases.Remove(key);
    }
    internal static void Shutdown()
    {
        Restore(); CancelWork(); enabled = false;
        foreach (var item in sprites.Values) if (item.Replacement != null) UObject.Destroy(item.Replacement);
        foreach (var item in atlases.Values) if (item.Replacement != null) UObject.Destroy(item.Replacement);
        foreach (var texture in files.Values) if (texture != null) UObject.Destroy(texture);
        sprites.Clear(); atlases.Clear(); files.Clear();
    }
    private static void Warn(string key,string message)
    { if (reported.Add(key)) Plugin.Global.Log.LogWarning("[UI Images] " + key + ": " + message); }
    private sealed class SpriteAsset
    {
        internal readonly Sprite Source; internal readonly Task<bool> Ready; internal Sprite Replacement; internal bool Rejected; internal long LastUse;
        internal SpriteAsset(Sprite source,Task<bool> ready) { Source = source; Ready = ready; }
    }
    private sealed class ImageWatch
    {
        internal readonly Image Image;
        internal Sprite Source, Overlay;
        internal bool Initialized;
        internal long NextCheck;
        internal ImageWatch(Image image) => Image = image;
    }
    private sealed class AtlasAsset
    {
        internal readonly Texture Source; internal Task<RgbaPixels> Ready; internal Texture2D Replacement; internal bool Rejected; internal long LastUse;
        internal AtlasAsset(Texture source,Task<RgbaPixels> ready) { Source = source; Ready = ready; }
    }
    private sealed record ImageBinding(Image Image,Sprite Base,Sprite OriginalOverride,Sprite Replacement);
    private sealed record SpineBinding(SkeletonGraphic Graphic,SkeletonDataAsset Asset,Texture Source,Texture2D Replacement,bool Multiple);
}
