using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace Gitster.Services.Features;

public sealed record UpdateCheckResult(
    bool HasUpdate,
    string CurrentVersion,
    string? LatestVersion,
    string? ReleaseUrl);

public sealed class UpdateCheckService
{
    private readonly HttpClient _http;

    public UpdateCheckService() : this(new HttpClient())
    {
    }

    public UpdateCheckService(HttpClient http)
    {
        _http = http;
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Gitster");
    }

    public async Task<UpdateCheckResult> CheckLatestReleaseAsync(
        string owner,
        string repo,
        string? currentVersion = null,
        CancellationToken ct = default)
    {
        var current = currentVersion ?? CurrentAppVersion();
        var url = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
        using var response = await _http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
            return new UpdateCheckResult(false, current, null, null);

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, cancellationToken: ct);
        var latest = release?.TagName;
        return new UpdateCheckResult(
            !string.IsNullOrWhiteSpace(latest) && IsNewerVersion(current, latest),
            current,
            latest,
            release?.HtmlUrl);
    }

    public static bool IsNewerVersion(string currentVersion, string latestVersion)
    {
        var current = Normalize(currentVersion);
        var latest = Normalize(latestVersion);
        return latest > current;
    }

    private static string CurrentAppVersion() =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";

    private static Version Normalize(string value)
    {
        var cleaned = value.Trim().TrimStart('v', 'V');
        var dash = cleaned.IndexOf('-', StringComparison.Ordinal);
        if (dash >= 0)
            cleaned = cleaned[..dash];

        return Version.TryParse(cleaned, out var version)
            ? version
            : new Version(0, 0);
    }

    private sealed class GitHubRelease
    {
        [System.Text.Json.Serialization.JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }
    }
}
