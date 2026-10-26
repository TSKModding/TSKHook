using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace TSKHook;

public sealed class ImagePackageManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string ResourceType { get; set; } = "ui-images";
    public string Version { get; set; }
    public string MinimumPluginVersion { get; set; } = "1.6.0";
    public string DownloadUrl { get; set; }
    public string Sha256 { get; set; }
    public long Size { get; set; }

    public void Validate(string pluginVersion)
    {
        if (SchemaVersion != 1 || ResourceType != "ui-images" || string.IsNullOrWhiteSpace(Version) || Version.Length > 100 ||
            !RuntimeAsset.IsSha256(Sha256) || Size <= 0 || Size > ImagePackageStore.MaxBytes)
            throw new InvalidDataException("Invalid image package manifest.");
        if (!System.Version.TryParse(MinimumPluginVersion, out var minimum) ||
            !System.Version.TryParse(pluginVersion.Split('-')[0], out var current) || current < minimum)
            throw new InvalidDataException("This image package requires a newer plugin.");
        PackageDownload.HttpsUri(DownloadUrl);
    }
}

public sealed class ImagePackageState
{
    public ImagePackageManifest Current { get; set; }
    public ImagePackageManifest Previous { get; set; }
}

/// <summary>Verified immutable image directories and an atomic pointer, independent of text packages.</summary>
public sealed class ImagePackageStore
{
    public const long MaxBytes = 128L * 1024 * 1024;
    private readonly string directory;
    private readonly SemaphoreSlim gate = new(1, 1);
    public ImagePackageStore(string directory) => this.directory = Path.GetFullPath(directory);
    private string StatePath => Path.Combine(directory, "active.json");
    public string PackagePath(ImagePackageManifest manifest)
    {
        if (!RuntimeAsset.IsSha256(manifest?.Sha256)) throw new InvalidDataException("Invalid image package identity.");
        return Path.Combine(directory, manifest.Sha256);
    }
    public ImagePackageState ReadState() => File.Exists(StatePath)
        ? JsonSerializer.Deserialize<ImagePackageState>(File.ReadAllText(StatePath), RuntimeAsset.JsonOptions)
          ?? throw new InvalidDataException("Invalid image state.") : new();

    public async Task<bool> UpdateAsync(HttpClient client, string manifestUrl, string pluginVersion,
        CancellationToken token = default, Action<ResourceUpdateProgress> progress = null)
    {
        var uri = PackageDownload.HttpsUri(manifestUrl);
        await gate.WaitAsync(token);
        try
        {
            progress?.Invoke(new("Checking"));
            using var buffer = new MemoryStream();
            await PackageDownload.CopyAsync(client, uri, buffer, 64 * 1024, token);
            var manifest = JsonSerializer.Deserialize<ImagePackageManifest>(buffer.ToArray(), RuntimeAsset.JsonOptions)
                ?? throw new InvalidDataException("Empty image manifest.");
            manifest.Validate(pluginVersion);
            var state = ReadState();
            var destination = PackagePath(manifest);
            if (state.Current?.Sha256 == manifest.Sha256 && Directory.Exists(destination))
            {
                try { VerifyDirectory(destination, manifest); return false; }
                catch (Exception e) when (e is InvalidDataException or IOException or JsonException) { }
            }
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N"));
            var zip = temporary + ".download";
            var replaced = temporary + ".old";
            try
            {
                using (var file = new FileStream(zip, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    await PackageDownload.CopyAsync(client, new Uri(manifest.DownloadUrl), file, manifest.Size, token, progress, manifest.Version);
                progress?.Invoke(new("Verifying", Version: manifest.Version));
                VerifyArchive(zip, manifest);
                Extract(zip, temporary, token);
                VerifyDirectory(temporary, manifest);
                token.ThrowIfCancellationRequested();
                progress?.Invoke(new("Installing", Version: manifest.Version));
                if (Directory.Exists(destination)) Directory.Move(destination, replaced);
                try
                {
                    Directory.Move(temporary, destination);
                    WriteState(new() { Current = manifest, Previous = state.Current?.Sha256 == manifest.Sha256 ? state.Previous : state.Current });
                }
                catch
                {
                    if (Directory.Exists(replaced))
                    {
                        if (Directory.Exists(destination)) Directory.Delete(destination, true);
                        Directory.Move(replaced, destination);
                    }
                    throw;
                }
                return true;
            }
            finally
            {
                if (File.Exists(zip)) File.Delete(zip);
                if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
                if (Directory.Exists(replaced)) Directory.Delete(replaced, true);
            }
        }
        finally { gate.Release(); }
    }

    public async Task SeedLocalAsync(string path, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            if (ReadState().Current != null || !File.Exists(Path.Combine(path, "manifest.json"))) return;
            var local = UiImageManifest.Read(path);
            // Local seed uses the exact manifest identity, which already contains all image hashes.
            var identity = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(path, "manifest.json")))).ToLowerInvariant();
            var manifest = new ImagePackageManifest { Version = local.Version, Sha256 = identity };
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(temporary);
                var names = local.Sprites.Select(x => x.File).Concat(local.Spine.SelectMany(x => x.Regions.Select(r => r.File))).Distinct();
                foreach (var name in names.Append("manifest.json"))
                {
                    token.ThrowIfCancellationRequested();
                    if (Path.GetFileName(name) != name) throw new InvalidDataException("Image packages use flat file names.");
                    File.Copy(Path.Combine(path, name), Path.Combine(temporary, name));
                }
                VerifyDirectory(temporary, manifest);
                var target = PackagePath(manifest);
                if (!Directory.Exists(target)) Directory.Move(temporary, target);
                else VerifyDirectory(target, manifest);
                WriteState(new() { Current = manifest });
            }
            finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
        }
        finally { gate.Release(); }
    }

    public async Task RollbackAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            var state = ReadState();
            if (state.Previous == null) throw new InvalidOperationException("No previous image package is available.");
            VerifyDirectory(PackagePath(state.Previous), state.Previous);
            token.ThrowIfCancellationRequested();
            WriteState(new() { Current = state.Previous, Previous = state.Current });
        }
        finally { gate.Release(); }
    }

    public static void VerifyArchive(string path, ImagePackageManifest manifest)
    {
        if (new FileInfo(path).Length != manifest.Size) throw new InvalidDataException("Image archive size mismatch.");
        using var file = File.OpenRead(path);
        using var sha = SHA256.Create();
        if (Convert.ToHexString(sha.ComputeHash(file)).ToLowerInvariant() != manifest.Sha256)
            throw new InvalidDataException("Image archive SHA-256 mismatch.");
    }
    public static void VerifyDirectory(string path, ImagePackageManifest manifest)
    {
        if (UiImageManifest.Read(path).Version != manifest.Version) throw new InvalidDataException("Image package version mismatch.");
    }
    public static void Extract(string archive, string target, CancellationToken token = default)
    {
        using var zip = ZipFile.OpenRead(archive);
        if (zip.Entries.Count == 0 || zip.Entries.Count > 513) throw new InvalidDataException("Too many image files.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        Directory.CreateDirectory(target);
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName;
            if (name.Contains('/') || name.Contains('\\') || name.Contains(':') || Path.GetFileName(name) != name ||
                !names.Add(name) || (name != "manifest.json" && !name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) ||
                entry.Length <= 0 || entry.Length > (name == "manifest.json" ? 1024 * 1024 : 4 * 1024 * 1024) ||
                (total += entry.Length) > MaxBytes)
                throw new InvalidDataException("Invalid image archive entry.");
            token.ThrowIfCancellationRequested();
            using var source = entry.Open();
            using var output = new FileStream(Path.Combine(target, name), FileMode.CreateNew);
            var buffer = new byte[65536];
            long copied = 0;
            int count;
            while ((count = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                token.ThrowIfCancellationRequested();
                if ((copied += count) > entry.Length) throw new InvalidDataException("Image entry exceeds declared length.");
                output.Write(buffer, 0, count);
            }
            if (copied != entry.Length) throw new InvalidDataException("Truncated image archive entry.");
        }
        if (!names.Contains("manifest.json")) throw new InvalidDataException("Missing image manifest.");
    }
    private void WriteState(ImagePackageState state)
    {
        var temporary = StatePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(state, RuntimeAsset.JsonOptions));
            File.Move(temporary, StatePath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
