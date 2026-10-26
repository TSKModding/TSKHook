using System.Net.Http;

namespace TSKHook;

public sealed record ResourceUpdateProgress(string Stage, long Bytes = 0, long Total = 0, string Version = null);

public static class PackageDownload
{
    public static Uri HttpsUri(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidDataException("Package URL must use HTTPS without credentials.");
        return uri;
    }

    public static async Task CopyAsync(HttpClient client, Uri uri, Stream target, long limit, CancellationToken token,
        Action<ResourceUpdateProgress> progress = null, string version = null)
    {
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri?.Scheme != "https") throw new InvalidDataException("Insecure redirect refused.");
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Download exceeds expected size.");
        using var source = await response.Content.ReadAsStreamAsync(token);
        var bytes = new byte[65536];
        long total = 0;
        var lastPercent = -1;
        int count;
        progress?.Invoke(new("Downloading", 0, limit, version));
        while ((count = await source.ReadAsync(bytes.AsMemory(), token)) > 0)
        {
            total += count;
            if (total > limit) throw new InvalidDataException("Download exceeds expected size.");
            await target.WriteAsync(bytes.AsMemory(0, count), token);
            var percent = (int)(total * 100 / limit);
            if (percent != lastPercent) { progress?.Invoke(new("Downloading", total, limit, version)); lastPercent = percent; }
        }
    }
}
