using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace TSKHook;

/// <summary>Read/verify/decode on a worker. Publishing does no disk IO or PNG decoding.</summary>
public sealed record PreparedImagePackage(string Directory, UiImageManifest Manifest, IReadOnlyDictionary<string, RgbaPixels> Pixels)
{
    public static PreparedImagePackage Load(string directory)
    {
        var manifest = UiImageManifest.Read(directory);
        var files = manifest.Sprites.Select(x => (x.File, x.Width, x.Height, x.Sha256))
            .Concat(manifest.Spine.SelectMany(x => x.Regions.Select(r => (r.File, r.Width, r.Height, r.Sha256))));
        var pixels = new Dictionary<string, RgbaPixels>(StringComparer.Ordinal);
        long total = 0;
        foreach (var file in files)
        {
            if (pixels.ContainsKey(file.File)) continue;
            total += (long)file.Width * file.Height * 4;
            if (total > 128L * 1024 * 1024) throw new InvalidDataException("Decoded image package exceeds 128 MiB.");
            var bytes = File.ReadAllBytes(UiImageManifest.AssetPath(directory,file.File));
            if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant() != file.Sha256)
                throw new InvalidDataException("Image changed while preparing package.");
            pixels.Add(file.File, PngPixels.Decode(bytes, file.Width, file.Height));
        }
        return new(directory, manifest, pixels);
    }
}

/// <summary>Package PNGs: 8-bit, non-interlaced RGBA/RGB/gray/indexed. No native decoder dependency.</summary>
public static class PngPixels
{
    public static RgbaPixels Decode(byte[] bytes, int width, int height)
    {
        if (width <= 0 || height <= 0 || width > 2048 || height > 2048 || bytes.Length < 33 ||
            !bytes.AsSpan(0,8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10})) throw new InvalidDataException("Invalid PNG.");
        using var compressed = new MemoryStream();
        byte[] palette = null, transparency = null;
        var channels = 0; var color = -1; var ended = false;
        for (var offset = 8; offset <= bytes.Length-12;)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset,4));
            if (length < 0 || length > bytes.Length-offset-12) throw new InvalidDataException("Truncated PNG chunk.");
            var name = Encoding.ASCII.GetString(bytes, offset+4, 4);
            var data = bytes.AsSpan(offset+8,length);
            if (name == "IHDR")
            {
                if (offset != 8 || length != 13 || BinaryPrimitives.ReadInt32BigEndian(data) != width ||
                    BinaryPrimitives.ReadInt32BigEndian(data.Slice(4)) != height || data[8] != 8 || data[10] != 0 || data[11] != 0 || data[12] != 0)
                    throw new InvalidDataException("Unsupported PNG header (requires non-interlaced 8-bit pixels).");
                color = data[9];
                channels = color switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => throw new InvalidDataException("Unsupported PNG color type.") };
            }
            else if (name == "IDAT") compressed.Write(data);
            else if (name == "PLTE") palette = data.ToArray();
            else if (name == "tRNS") transparency = data.ToArray();
            else if (name == "IEND") { ended = true; break; }
            else if ((bytes[offset+4] & 32) == 0) throw new InvalidDataException("Unsupported critical PNG chunk.");
            offset += length+12;
        }
        if (channels == 0 || !ended || (color == 3 && (palette == null || palette.Length % 3 != 0)))
            throw new InvalidDataException("Incomplete PNG.");
        compressed.Position = 0;
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        var stride = checked(width*channels);
        var row = new byte[stride]; var previous = new byte[stride];
        var rgba = new byte[checked(width*height*4)];
        for (var y = 0; y < height; y++)
        {
            var filter = zlib.ReadByte();
            if (filter < 0 || filter > 4) throw new InvalidDataException("Invalid PNG filter.");
            var read = 0;
            while (read < row.Length) { var n = zlib.Read(row,read,row.Length-read); if (n == 0) throw new InvalidDataException("Truncated PNG pixels."); read += n; }
            for (var i = 0; i < stride; i++)
            {
                var a = i >= channels ? row[i-channels] : 0; var b = previous[i]; var c = i >= channels ? previous[i-channels] : 0;
                row[i] = unchecked((byte)(row[i] + (filter switch { 1 => a, 2 => b, 3 => (a+b)/2, 4 => Paeth(a,b,c), _ => 0 })));
            }
            for (var x = 0; x < width; x++)
            {
                var i = x*channels; var d = ((height-1-y)*width+x)*4;
                byte r, g, b, a = 255;
                if (color == 3)
                {
                    var p = row[i]; if (p*3+2 >= palette.Length) throw new InvalidDataException("PNG palette index out of bounds.");
                    r = palette[p*3]; g = palette[p*3+1]; b = palette[p*3+2];
                    if (transparency != null && p < transparency.Length) a = transparency[p];
                }
                else if (color is 0 or 4)
                {
                    r = g = b = row[i];
                    if (color == 4) a = row[i+1];
                    else if (transparency?.Length == 2 && BinaryPrimitives.ReadUInt16BigEndian(transparency) == r) a = 0;
                }
                else
                {
                    r = row[i]; g = row[i+1]; b = row[i+2];
                    if (color == 6) a = row[i+3];
                    else if (transparency?.Length == 6 && BinaryPrimitives.ReadUInt16BigEndian(transparency) == r &&
                        BinaryPrimitives.ReadUInt16BigEndian(transparency.AsSpan(2)) == g && BinaryPrimitives.ReadUInt16BigEndian(transparency.AsSpan(4)) == b) a = 0;
                }
                rgba[d] = r; rgba[d+1] = g; rgba[d+2] = b; rgba[d+3] = a;
            }
            (row, previous) = (previous, row);
        }
        if (zlib.ReadByte() != -1) throw new InvalidDataException("Excess PNG pixels.");
        return new(width,height,rgba);
    }
    private static int Paeth(int a, int b, int c)
    {
        var p = a+b-c; var pa = Math.Abs(p-a); var pb = Math.Abs(p-b); var pc = Math.Abs(p-c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
