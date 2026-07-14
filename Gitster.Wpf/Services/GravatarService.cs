using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;

using Gitster.Core;

namespace Gitster.Services;

/// <summary>
/// Fetches and caches Gravatar avatars (plan A14). Off by default; only ever called when
/// the user opts in. Misses are remembered so we don't re-hit the network, and nothing
/// here blocks the UI — callers show initials first and swap the image in when it arrives.
/// </summary>
public static class GravatarService
{
    private const int MaxCachedEntries = 200;
    private static readonly object CachePruneLock = new();
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    private static readonly string CacheDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Gitster", "avatars");

    /// <summary>Two upper-case initials for the initials-circle fallback.</summary>
    public static string Initials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var parts = name.Trim().Split([' ', '.', '-', '_'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "?";
        if (parts.Length == 1)
            return parts[0].Length >= 2 ? parts[0][..2].ToUpperInvariant() : parts[0].ToUpperInvariant();
        return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[^1][0])}";
    }

    /// <summary>A stable color seeded from the email/name, for the initials circle.</summary>
    public static System.Windows.Media.Color ColorFor(string? seed)
    {
        var s = seed ?? string.Empty;
        int hash = 0;
        foreach (var c in s) hash = unchecked(hash * 31 + c);
        var hue = Math.Abs(hash) % 360;
        return FromHsl(hue, 0.45, 0.55);
    }

    /// <summary>
    /// Returns a cached or freshly fetched avatar for <paramref name="email"/>, or null when
    /// the user has no Gravatar / is offline (caller falls back to initials).
    /// </summary>
    public static async Task<BitmapImage?> GetAvatarAsync(string? email, int size = 48)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;

        var hash = Md5(email.Trim().ToLowerInvariant());
        var file = Path.Combine(CacheDir, $"{hash}_{size}.png");
        var miss = Path.Combine(CacheDir, $"{hash}_{size}.none");

        try
        {
            if (File.Exists(miss))
            {
                Touch(miss);
                return null;
            }

            if (File.Exists(file))
            {
                Touch(file);
                return Load(file);
            }

            Directory.CreateDirectory(CacheDir);
            PruneCacheDirectory(CacheDir, MaxCachedEntries);
            var url = $"https://www.gravatar.com/avatar/{hash}?s={size}&d=404";
            using var resp = await Http.GetAsync(url);
            if (!resp.IsSuccessStatusCode)
            {
                try { await File.WriteAllTextAsync(miss, string.Empty); } catch { /* ignore */ }
                PruneCacheDirectory(CacheDir, MaxCachedEntries);
                return null;
            }

            var bytes = await resp.Content.ReadAsByteArrayAsync();
            await File.WriteAllBytesAsync(file, bytes);
            PruneCacheDirectory(CacheDir, MaxCachedEntries);
            return Load(file);
        }
        catch
        {
            return null; // offline or any failure → initials fallback
        }
    }

    internal static int PruneCacheDirectory(string cacheDir, int maxEntries)
    {
        if (maxEntries < 1)
            throw new ArgumentOutOfRangeException(nameof(maxEntries), "Cache must keep at least one entry.");

        if (!Directory.Exists(cacheDir))
            return 0;

        lock (CachePruneLock)
        {
            var files = Directory.EnumerateFiles(cacheDir, "*.*", SearchOption.TopDirectoryOnly)
                .Where(path => path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".none", StringComparison.OrdinalIgnoreCase))
                .Select(path => new FileInfo(path))
                .OrderByDescending(info => info.LastAccessTimeUtc)
                .ThenByDescending(info => info.LastWriteTimeUtc)
                .ThenBy(info => info.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var deleted = 0;
            foreach (var file in files.Skip(maxEntries))
            {
                try
                {
                    file.Delete();
                    deleted++;
                }
                catch
                {
                    // Best effort: an in-use cache file should never break avatar fallback.
                }
            }

            return deleted;
        }
    }

    private static void Touch(string path)
    {
        try
        {
            File.SetLastAccessTimeUtc(path, DateTime.UtcNow);
        }
        catch
        {
            // Cache recency is only an optimization.
        }
    }

    private static BitmapImage Load(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(path);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private static string Md5(string input)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(input));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    private static System.Windows.Media.Color FromHsl(double h, double s, double l)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        double m = l - c / 2;
        double r = 0, g = 0, b = 0;
        if (h < 60) { r = c; g = x; }
        else if (h < 120) { r = x; g = c; }
        else if (h < 180) { g = c; b = x; }
        else if (h < 240) { g = x; b = c; }
        else if (h < 300) { r = x; b = c; }
        else { r = c; b = x; }
        return System.Windows.Media.Color.FromRgb(
            (byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}
