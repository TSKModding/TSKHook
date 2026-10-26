using UnityEngine;
using UObject = UnityEngine.Object;

namespace TSKHook;

internal static class UiImagePixels
{
    internal static Texture2D Read(Texture source)
    {
        if (source.width <= 0 || source.height <= 0 || source.width > 8192 || source.height > 8192)
            throw new InvalidDataException("Unsupported atlas dimensions.");
        var previous = RenderTexture.active;
        var temporary = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Texture2D output = null;
        try
        {
            Graphics.Blit(source, temporary);
            RenderTexture.active = temporary;
            output = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, false);
            output.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false);
            // ReadPixels already populated CPU memory. This snapshot is never displayed; do not upload it back to the GPU.
            return output;
        }
        catch { if (output != null) UObject.Destroy(output); throw; }
        finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(temporary); }
    }
}
