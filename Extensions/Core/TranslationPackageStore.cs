using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace TSKHook;

public sealed class TranslationManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string Version { get; set; }
    public string MinimumPluginVersion { get; set; } = "1.4.0";
    public string DownloadUrl { get; set; }
    public string Sha256 { get; set; }
    public long Size { get; set; }
    public int Entries { get; set; }

    public void Validate(string pluginVersion)
    {
        if (SchemaVersion != 1 || string.IsNullOrWhiteSpace(Version) || Version.Length > 100 ||
            !RuntimeAsset.IsSha256(Sha256) || Size <= 0 || Size > TranslationPackageStore.MaxPackageBytes || Entries <= 0)
            throw new InvalidDataException("Invalid translation manifest.");
        if (!System.Version.TryParse(MinimumPluginVersion, out var minimum) ||
            !System.Version.TryParse(pluginVersion.Split('-')[0], out var current) || current < minimum)
            throw new InvalidDataException("This translation package requires a newer plugin.");
        if (!Uri.TryCreate(DownloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidDataException("Translation download URL must use HTTPS without credentials.");
    }
}

public sealed class PackageState
{
    public TranslationManifest Current { get; set; }
    public TranslationManifest Previous { get; set; }
}

/// <summary>Immutable package files and one atomic active-state pointer; failed updates never overwrite the current package.</summary>
public sealed class TranslationPackageStore
{
    public const long MaxPackageBytes = 128L * 1024 * 1024;
    private readonly string directory;
    private readonly SemaphoreSlim gate = new(1, 1);
    public TranslationPackageStore(string directory) => this.directory = Path.GetFullPath(directory);
    private string StatePath => Path.Combine(directory, "active.json");
    public string PackagePath(TranslationManifest manifest)
    {
        if (!RuntimeAsset.IsSha256(manifest?.Sha256)) throw new InvalidDataException("Invalid package identity.");
        return Path.Combine(directory, manifest.Sha256 + ".jsonl");
    }

    public PackageState ReadState() => File.Exists(StatePath)
        ? JsonSerializer.Deserialize<PackageState>(File.ReadAllText(StatePath), RuntimeAsset.JsonOptions)
          ?? throw new InvalidDataException("Invalid package state.")
        : new PackageState();

    public async Task SeedLocalAsync(string path, CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            if (ReadState().Current != null || !File.Exists(path)) return;
            var index = RuntimeAsset.Load(path);
            using var source = File.OpenRead(path);
            using var sha = SHA256.Create();
            var manifest = new TranslationManifest { Version = "legacy-local", Entries = index.ExactCount,
                Size = source.Length, Sha256 = Convert.ToHexString(sha.ComputeHash(source)).ToLowerInvariant(),
                DownloadUrl = "https://local.invalid/ui_translations.jsonl" };
            Directory.CreateDirectory(directory);
            var target = PackagePath(manifest);
            var temporary = target + ".seed";
            try
            {
                source.Position = 0;
                using (var destination = File.Create(temporary)) await source.CopyToAsync(destination, token);
                Verify(temporary, manifest);
                File.Move(temporary, target, true);
                WriteState(new PackageState { Current = manifest });
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally { gate.Release(); }
    }

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
            var manifest = JsonSerializer.Deserialize<TranslationManifest>(buffer.ToArray(), RuntimeAsset.JsonOptions)
                           ?? throw new InvalidDataException("Empty manifest.");
            manifest.Validate(pluginVersion);
            var state = ReadState();
            var destination = PackagePath(manifest);
            if (state.Current?.Sha256 == manifest.Sha256 && File.Exists(destination))
            {
                try { Verify(destination, manifest); return false; }
                catch (Exception e) when (e is InvalidDataException or IOException) { /* Redownload a corrupted cached package. */ }
            }
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".download");
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    await PackageDownload.CopyAsync(client, new Uri(manifest.DownloadUrl), file, manifest.Size, token, progress, manifest.Version);
                progress?.Invoke(new("Verifying", Version: manifest.Version));
                Verify(temporary, manifest);
                token.ThrowIfCancellationRequested();
                progress?.Invoke(new("Installing", Version: manifest.Version));
                File.Move(temporary, destination, true);
                WriteState(new PackageState { Current = manifest, Previous = state.Current?.Sha256 == manifest.Sha256 ? state.Previous : state.Current });
                return true;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally { gate.Release(); }
    }

    public async Task RollbackAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token);
        try
        {
            var state = ReadState();
            if (state.Previous == null) throw new InvalidOperationException("No previous translation package is available.");
            Verify(PackagePath(state.Previous), state.Previous);
            token.ThrowIfCancellationRequested();
            WriteState(new PackageState { Current = state.Previous, Previous = state.Current });
        }
        finally { gate.Release(); }
    }

    public static void Verify(string path, TranslationManifest manifest)
    {
        var info = new FileInfo(path);
        if (info.Length != manifest.Size) throw new InvalidDataException("Translation package size mismatch.");
        using (var file = File.OpenRead(path))
        using (var sha = SHA256.Create())
            if (Convert.ToHexString(sha.ComputeHash(file)).ToLowerInvariant() != manifest.Sha256)
                throw new InvalidDataException("Translation package SHA-256 mismatch.");
        if (RuntimeAsset.Load(path).ExactCount != manifest.Entries)
            throw new InvalidDataException("Translation package entry count mismatch.");
    }

    private void WriteState(PackageState state)
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
