using System.Security.Cryptography;
using System.Text.Json;

namespace TSKHook;

public sealed class UiImageManifest
{
    public int SchemaVersion { get; set; }
    public string Version { get; set; }
    public List<UiSpriteRule> Sprites { get; set; } = new();
    public List<UiSpineRule> Spine { get; set; } = new();
    public bool HasSameRules(UiImageManifest other) => other != null &&
        JsonSerializer.Serialize(Sprites) == JsonSerializer.Serialize(other.Sprites) &&
        JsonSerializer.Serialize(Spine) == JsonSerializer.Serialize(other.Spine);
    public static UiImageManifest Read(string directory)
    {
        var manifest = JsonSerializer.Deserialize<UiImageManifest>(File.ReadAllText(Path.Combine(directory, "manifest.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("Missing image manifest.");
        if (manifest.SchemaVersion != 1 || string.IsNullOrWhiteSpace(manifest.Version) || manifest.Sprites.Count + manifest.Spine.Count > 512)
            throw new InvalidDataException("Unsupported image manifest.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in manifest.Sprites)
        {
            ValidateAsset(directory, rule.File, rule.Sha256, rule.Width, rule.Height);
            ValidateHash(rule.SourceRgbaSha256);
            if (string.IsNullOrEmpty(rule.Sprite) || !keys.Add("sprite:" + rule.Sprite)) throw new InvalidDataException("Duplicate/empty sprite rule.");
        }
        foreach (var rule in manifest.Spine)
        {
            if (string.IsNullOrEmpty(rule.Asset) || string.IsNullOrEmpty(rule.Texture) || !keys.Add("spine:" + rule.Asset) || rule.Regions.Count == 0)
                throw new InvalidDataException("Duplicate/empty Spine rule.");
            var regions = new HashSet<string>(StringComparer.Ordinal);
            foreach (var region in rule.Regions)
            {
                ValidateAsset(directory, region.File, region.Sha256, region.Width, region.Height);
                ValidateHash(region.SourceRgbaSha256);
                if (!regions.Add(region.Name) || string.IsNullOrEmpty(region.Name) || region.Degrees is not (0 or 90) ||
                    region.OriginalWidth < region.Width || region.OriginalHeight < region.Height || region.OffsetX < 0 || region.OffsetY < 0 ||
                    region.OffsetX + region.Width > region.OriginalWidth || region.OffsetY + region.Height > region.OriginalHeight)
                    throw new InvalidDataException("Invalid Spine region geometry.");
            }
        }
        return manifest;
    }
    public static string AssetPath(string root, string file)
    {
        if (string.IsNullOrWhiteSpace(file) || Path.IsPathRooted(file)) throw new InvalidDataException("Invalid image asset path.");
        var directory = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(directory, file));
        if (!path.StartsWith(directory, StringComparison.OrdinalIgnoreCase) || !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Image asset escaped package directory.");
        return path;
    }
    private static void ValidateHash(string hash)
    {
        if (hash == null || hash.Length != 64 || !hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
            throw new InvalidDataException("Invalid image SHA-256.");
    }
    private static void ValidateAsset(string root, string file, string hash, int width, int height)
    {
        ValidateHash(hash);
        var bytes = File.ReadAllBytes(AssetPath(root, file));
        if (bytes.Length < 24 || bytes.Length > 4 * 1024 * 1024 || width <= 0 || height <= 0 || width > 2048 || height > 2048 ||
            !bytes.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}) ||
            System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)) != width ||
            System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)) != height ||
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != hash)
            throw new InvalidDataException("Image size or checksum mismatch: " + file);
    }
}
public sealed class UiSpriteRule
{
    public string Sprite { get; set; }
    public string File { get; set; }
    public string Sha256 { get; set; }
    public string SourceRgbaSha256 { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}
public sealed class UiSpineRule
{
    public string Asset { get; set; }
    public string Texture { get; set; }
    public List<UiSpineRegionRule> Regions { get; set; } = new();
}
public sealed class UiSpineRegionRule
{
    public string Name { get; set; }
    public string File { get; set; }
    public string Sha256 { get; set; }
    public string SourceRgbaSha256 { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Degrees { get; set; }
    public int OriginalWidth { get; set; }
    public int OriginalHeight { get; set; }
    public int OffsetX { get; set; }
    public int OffsetY { get; set; }
}
