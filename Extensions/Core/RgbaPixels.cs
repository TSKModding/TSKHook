using System.Numerics;
using System.Security.Cryptography;

namespace TSKHook;

/// <summary>Owned, bottom-up RGBA bytes; no Unity objects may enter the worker pipeline.</summary>
public sealed record RgbaPixels(int Width, int Height, byte[] Bytes)
{
    public string CanonicalHash()
    {
        var canonical = new byte[Bytes.Length];
        for (var y = 0; y < Height; y++) for (var x = 0; x < Width; x++)
        {
            var source = ((Height - 1 - y) * Width + x) * 4;
            var dest = (y * Width + x) * 4;
            if (Bytes[source + 3] != 0) Buffer.BlockCopy(Bytes, source, canonical, dest, 3);
            canonical[dest + 3] = Bytes[source + 3];
        }
        return Convert.ToHexString(SHA256.HashData(canonical)).ToLowerInvariant();
    }

    public RgbaPixels Sprite(int width, int height, Vector2[] vertices, Vector2[] uv, ushort[] triangles,
        float pixelsPerUnit, Vector2 pivot, bool flipRows = false)
    {
        var pixels = new byte[checked(width * height * 4)];
        for (var t = 0; t < triangles.Length; t += 3)
        {
            var ia = triangles[t]; var ib = triangles[t + 1]; var ic = triangles[t + 2];
            var a = vertices[ia] * pixelsPerUnit + pivot;
            var b = vertices[ib] * pixelsPerUnit + pivot;
            var c = vertices[ic] * pixelsPerUnit + pivot;
            var denominator = (b.Y-c.Y)*(a.X-c.X)+(c.X-b.X)*(a.Y-c.Y);
            if (Math.Abs(denominator) < 0.00001f) continue;
            var minX = Math.Max(0, (int)MathF.Floor(Math.Min(a.X, Math.Min(b.X,c.X))));
            var maxX = Math.Min(width-1, (int)MathF.Ceiling(Math.Max(a.X, Math.Max(b.X,c.X))));
            var minY = Math.Max(0, (int)MathF.Floor(Math.Min(a.Y, Math.Min(b.Y,c.Y))));
            var maxY = Math.Min(height-1, (int)MathF.Ceiling(Math.Max(a.Y, Math.Max(b.Y,c.Y))));
            for (var y = minY; y <= maxY; y++) for (var x = minX; x <= maxX; x++)
            {
                var wa = ((b.Y-c.Y)*(x+0.5f-c.X)+(c.X-b.X)*(y+0.5f-c.Y))/denominator;
                var wb = ((c.Y-a.Y)*(x+0.5f-c.X)+(a.X-c.X)*(y+0.5f-c.Y))/denominator;
                var wc = 1-wa-wb;
                if (wa < -0.00001f || wb < -0.00001f || wc < -0.00001f) continue;
                var coordinate = uv[ia]*wa+uv[ib]*wb+uv[ic]*wc;
                var sx = Math.Clamp((int)MathF.Floor(coordinate.X*Width),0,Width-1);
                var sy = Math.Clamp((int)MathF.Floor(coordinate.Y*Height),0,Height-1);
                if (flipRows) sy = Height-1-sy;
                Buffer.BlockCopy(Bytes, (sy*Width+sx)*4, pixels, (y*width+x)*4, 4);
            }
        }
        return new(width, height, pixels);
    }

    public RgbaPixels Region(PixelRegion region, bool flipRows = false)
    {
        var pixels = new byte[checked(region.Width * region.Height * 4)];
        for (var y = 0; y < region.Height; y++) for (var x = 0; x < region.Width; x++)
        {
            var ax = region.X + (region.Degrees == 90 ? region.Height-1-y : x);
            var topY = region.Y + (region.Degrees == 90 ? region.Width-1-x : region.Height-1-y);
            Buffer.BlockCopy(Bytes, (((flipRows ? topY : Height-1-topY))*Width+ax)*4, pixels, (y*region.Width+x)*4, 4);
        }
        return new(region.Width, region.Height, pixels);
    }

    public RgbaPixels Copy(bool flipRows)
    {
        var copy = new byte[Bytes.Length];
        for (var y = 0; y < Height; y++) Buffer.BlockCopy(Bytes,(flipRows ? Height-1-y : y)*Width*4,copy,y*Width*4,Width*4);
        return new(Width,Height,copy);
    }

    public void Patch(PixelRegion region, RgbaPixels replacement)
    {
        if (replacement.Width != region.Width || replacement.Height != region.Height)
            throw new InvalidDataException("Replacement dimensions differ from atlas region.");
        var w = region.Degrees == 90 ? region.Height : region.Width;
        var h = region.Degrees == 90 ? region.Width : region.Height;
        for (var py = -1; py <= h; py++) for (var px = -1; px <= w; px++)
        {
            var cx = Math.Clamp(px, 0, w-1); var cy = Math.Clamp(py, 0, h-1);
            var x = region.Degrees == 90 ? region.Width-1-cy : cx;
            var y = region.Degrees == 90 ? region.Height-1-cx : region.Height-1-cy;
            var ax = region.X+px; var ay = Height-1-region.Y-py;
            if (ax >= 0 && ax < Width && ay >= 0 && ay < Height)
                Buffer.BlockCopy(replacement.Bytes, (y*region.Width+x)*4, Bytes, (ay*Width+ax)*4, 4);
        }
    }
}

public sealed record PixelRegion(int X, int Y, int Width, int Height, int Degrees);
