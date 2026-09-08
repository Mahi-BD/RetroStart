using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace WinlyStart.Core;

/// <summary>
/// Fetches the two things a website tile needs: its favicon (used as the tile/list icon) and a
/// preview image for the live face (Open Graph / Twitter card image, which is what a site publishes
/// as its own thumbnail). Everything is cached under %LocalAppData%\WinlyStart\web so a tile costs
/// one request per refresh interval, never one per repaint.
/// </summary>
internal static partial class WebAssets
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly SemaphoreSlim Gate = new(3);          // be polite: at most 3 in flight
    public static string CacheDir => Path.Combine(Store.Dir, "web");

    /// <summary>How long a cached thumbnail stays fresh.</summary>
    public static readonly TimeSpan ThumbnailLifetime = TimeSpan.FromMinutes(30);

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 5 };
        var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) WinlyStart/1.4");
        c.DefaultRequestHeaders.AcceptEncoding.ParseAdd("identity");
        c.MaxResponseContentBufferSize = 8 * 1024 * 1024;
        return c;
    }

    [GeneratedRegex("<link[^>]+rel\\s*=\\s*[\"'][^\"']*icon[^\"']*[\"'][^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex IconLinkRegex();
    [GeneratedRegex("href\\s*=\\s*[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase)]
    private static partial Regex HrefRegex();
    [GeneratedRegex("<meta[^>]+(?:property|name)\\s*=\\s*[\"'](?:og:image(?::secure_url)?|twitter:image(?::src)?)[\"'][^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex OgImageRegex();
    [GeneratedRegex("content\\s*=\\s*[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase)]
    private static partial Regex ContentRegex();

    private static string Hash(string s)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
    }

    /// <summary>Downloads the site's favicon and returns the cached file, or null.</summary>
    public static async Task<string?> GetIconAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !IsWeb(uri)) return null;
        string path = Path.Combine(CacheDir, Hash(uri.Host) + "-icon.png");
        if (File.Exists(path) && new FileInfo(path).Length > 0) return path;

        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            string? href = null;
            if (await GetStringAsync(uri).ConfigureAwait(false) is { } html)
            {
                // prefer a declared icon (usually higher resolution than /favicon.ico)
                var best = IconLinkRegex().Matches(html)
                    .Select(m => HrefRegex().Match(m.Value))
                    .Where(m => m.Success)
                    .Select(m => m.Groups[1].Value)
                    .LastOrDefault();
                if (best != null && Uri.TryCreate(uri, best, out var abs)) href = abs.ToString();
            }
            href ??= new Uri(uri, "/favicon.ico").ToString();
            return await DownloadAsync(href, path).ConfigureAwait(false);
        }
        catch { return null; }
        finally { Gate.Release(); }
    }

    /// <summary>Downloads the page's own preview image (og:image) and returns the cached file, or null.</summary>
    public static async Task<string?> GetThumbnailAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !IsWeb(uri)) return null;
        string path = Path.Combine(CacheDir, Hash(url) + "-thumb.png");
        if (File.Exists(path) && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < ThumbnailLifetime
            && new FileInfo(path).Length > 0) return path;

        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (await GetStringAsync(uri).ConfigureAwait(false) is not { } html) return Existing(path);
            var m = OgImageRegex().Match(html);
            if (!m.Success) return Existing(path);
            var c = ContentRegex().Match(m.Value);
            if (!c.Success || !Uri.TryCreate(uri, c.Groups[1].Value, out var img)) return Existing(path);
            return await DownloadAsync(img.ToString(), path).ConfigureAwait(false) ?? Existing(path);
        }
        catch { return Existing(path); }
        finally { Gate.Release(); }

        static string? Existing(string p) => File.Exists(p) ? p : null;   // stale beats nothing
    }

    private static bool IsWeb(Uri u) => u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps;

    private static async Task<string?> GetStringAsync(Uri uri)
    {
        try
        {
            using var res = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return null;
            var type = res.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (!type.Contains("html", StringComparison.OrdinalIgnoreCase)) return null;
            var bytes = await res.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            // only the <head> matters for icons/og tags
            return Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 256 * 1024));
        }
        catch { return null; }
    }

    private static async Task<string?> DownloadAsync(string url, string path)
    {
        try
        {
            using var res = await Http.GetAsync(url).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return null;
            var bytes = await res.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            if (bytes.Length < 64) return null;
            Directory.CreateDirectory(CacheDir);
            await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
            return path;
        }
        catch { return null; }
    }
}
