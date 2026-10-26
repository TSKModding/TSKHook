using System.Diagnostics;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UObject = UnityEngine.Object;

namespace TSKHook;

/// <summary>Main-thread GPU submission/polling, shared immutable CPU atlas snapshots, no synchronous waits.</summary>
internal static class UiTextureReadback
{
    private const long CacheBytes = 128L * 1024 * 1024;
    private static readonly Dictionary<int, Entry> Entries = new();
    private static readonly Queue<Entry> Waiting = new();
    private static Entry active;
    private static long retained;
    private static bool checkedApi, asynchronous;
    private static RequestTexture requestTexture;
    private static RequestBool done, failed;
    private static RequestInt dataSize;
    private static RequestData data;

    // ABI from Unity 2021.3's public reference source. The game strips the C# request wrapper,
    // but retains these engine icalls. Resolve all of them before submitting anything.
    [StructLayout(LayoutKind.Sequential)]
    private struct Request { public IntPtr Pointer; public int Version; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void RequestTexture(IntPtr texture, int mip, GraphicsFormat format, IntPtr destination, out Request request);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)] private delegate bool RequestBool(ref Request request);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int RequestInt(ref Request request);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr RequestData(ref Request request, int layer);

    private sealed class Entry
    {
        internal Texture Source;
        internal int Width, Height;
        internal RenderTexture Target;
        internal Request Request;
        internal readonly TaskCompletionSource<RgbaPixels> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal long LastUse, Bytes;
        internal bool Discard;
    }

    internal static Task<RgbaPixels> Get(Texture source)
    {
        var id = source.GetInstanceID();
        if (Entries.TryGetValue(id, out var entry) && entry.Source == source)
        { entry.LastUse = Stopwatch.GetTimestamp(); return entry.Result.Task; }
        var width = source.width; var height = source.height;
        if (width <= 0 || height <= 0 || width > 8192 || height > 8192) throw new InvalidDataException("Unsupported atlas dimensions.");
        entry = new Entry { Source = source, Width = width, Height = height, LastUse = Stopwatch.GetTimestamp() };
        Entries[id] = entry;
        Waiting.Enqueue(entry);
        return entry.Result.Task;
    }

    internal static void Update(bool allowNewWork)
    {
        if (active != null)
        {
            try
            {
                if (!done(ref active.Request)) return;
                if (failed(ref active.Request)) throw new InvalidDataException("GPU readback failed; keeping original artwork.");
                var length = checked(active.Width*active.Height*4);
                if (dataSize(ref active.Request) != length) throw new InvalidDataException("Unexpected GPU readback size.");
                if (!active.Discard)
                {
                    var bytes = new byte[length];
                    var pointer = data(ref active.Request, 0);
                    if (pointer == IntPtr.Zero) throw new InvalidDataException("GPU readback returned no pixels.");
                    Marshal.Copy(pointer, bytes, 0, bytes.Length);
                    Complete(active, new(active.Width, active.Height, bytes));
                }
                else active.Result.TrySetCanceled();
            }
            catch (Exception e) { active.Result.TrySetException(e); }
            finally
            {
                // A request still in flight must retain its render target until Unity reports done.
                if (active != null && (active.Result.Task.IsCompleted || active.Discard && done(ref active.Request)))
                { RenderTexture.ReleaseTemporary(active.Target); active = null; }
            }
            return;
        }
        if (!allowNewWork || Waiting.Count == 0) return;
        CheckApi();
        var next = Waiting.Dequeue();
        if (next.Discard || next.Source == null) { next.Result.TrySetCanceled(); return; }
        try
        {
            if (asynchronous)
            {
                next.Target = RenderTexture.GetTemporary(next.Width,next.Height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
                Graphics.Blit(next.Source,next.Target);
                requestTexture(IL2CPP.Il2CppObjectBaseToPtr(next.Target),0,GraphicsFormat.R8G8B8A8_SRGB,IntPtr.Zero,out next.Request);
                active = next;
            }
            else
            {
                // Compatibility path only runs outside battle, one atlas per frame, and shares its result.
                var texture = UiImagePixels.Read(next.Source);
                try
                {
                    var raw = texture.GetRawTextureData();
                    var bytes = new byte[checked(next.Width*next.Height*4)];
                    if (raw.Length != bytes.Length) throw new InvalidDataException("Unexpected CPU atlas size.");
                    // Bulk copy avoids millions of IL2CPP indexer calls.
                    Marshal.Copy(IntPtr.Add(raw.Pointer, IntPtr.Size*4),bytes,0,bytes.Length);
                    Complete(next,new(next.Width,next.Height,bytes));
                }
                finally { UObject.Destroy(texture); }
            }
        }
        catch (Exception e)
        {
            if (next.Target != null) RenderTexture.ReleaseTemporary(next.Target);
            next.Result.TrySetException(e);
        }
    }

    private static void Complete(Entry entry, RgbaPixels pixels)
    {
        entry.Bytes = pixels.Bytes.LongLength; retained += entry.Bytes;
        entry.Result.TrySetResult(pixels);
        Trim();
    }
    private static void Trim()
    {
        // Only completed CPU snapshots are evicted. Workers retain their own Task/result lease.
        var now = Stopwatch.GetTimestamp();
        foreach (var pair in Entries.ToArray())
            if (pair.Value.Result.Task.IsCompleted && (retained > CacheBytes || pair.Value.Source == null ||
                now-pair.Value.LastUse > 15*Stopwatch.Frequency))
            { retained -= pair.Value.Bytes; Entries.Remove(pair.Key); }
    }
    internal static void Clear()
    {
        foreach (var entry in Entries.Values)
        {
            entry.Discard = true;
            if (entry != active) entry.Result.TrySetCanceled();
        }
        Entries.Clear(); Waiting.Clear(); retained = 0;
        // No WaitForCompletion/WaitAllRequests. Update drains the active request even while images are off.
    }
    private static T Resolve<T>(string name) where T : Delegate
    {
        var pointer = IL2CPP.il2cpp_resolve_icall(name);
        if (pointer == IntPtr.Zero) throw new NotSupportedException(name);
        return Marshal.GetDelegateForFunctionPointer<T>(pointer);
    }
    private static void CheckApi()
    {
        if (checkedApi) return;
        checkedApi = true;
        try
        {
            if (IntPtr.Size != 8 || !Application.unityVersion.StartsWith("2021.3.",StringComparison.Ordinal) || !SystemInfo.supportsAsyncGPUReadback)
                throw new NotSupportedException("Unity version or graphics backend");
            requestTexture = Resolve<RequestTexture>("UnityEngine.Rendering.AsyncGPUReadback::Request_Internal_Texture_2_Injected");
            done = Resolve<RequestBool>("UnityEngine.Rendering.AsyncGPUReadbackRequest::IsDone_Injected");
            failed = Resolve<RequestBool>("UnityEngine.Rendering.AsyncGPUReadbackRequest::HasError_Injected");
            dataSize = Resolve<RequestInt>("UnityEngine.Rendering.AsyncGPUReadbackRequest::GetLayerDataSize_Injected");
            data = Resolve<RequestData>("UnityEngine.Rendering.AsyncGPUReadbackRequest::GetDataRaw_Injected");
            asynchronous = true;
            Plugin.Global.Log.LogInfo("[UI Images] Shared asynchronous GPU readback enabled (Unity 2021.3).");
        }
        catch (Exception e)
        {
            Plugin.Global.Log.LogInfo("[UI Images] Async readback unavailable; shared compatibility readbacks are deferred until outside battle: " + e.Message);
        }
    }
}
