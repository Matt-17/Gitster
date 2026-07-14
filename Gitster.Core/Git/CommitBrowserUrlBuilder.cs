using System.Text.RegularExpressions;

namespace Gitster.Core.Git;

public static partial class CommitBrowserUrlBuilder
{
    public static bool TryBuild(string remoteUrl, string sha, out string browserUrl)
    {
        browserUrl = string.Empty;
        if (string.IsNullOrWhiteSpace(remoteUrl) || string.IsNullOrWhiteSpace(sha))
            return false;

        if (TryBuildAzureSsh(remoteUrl.Trim(), sha, out browserUrl))
            return true;

        if (!TryParseRemote(remoteUrl.Trim(), out var host, out var path))
            return false;

        path = NormalizePath(path);
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(path))
            return false;

        if (host.Equals("ssh.dev.azure.com", StringComparison.OrdinalIgnoreCase))
            return TryBuildAzurePath(path, sha, out browserUrl);

        var lowerHost = host.ToLowerInvariant();
        if (lowerHost.Contains("dev.azure.com", StringComparison.OrdinalIgnoreCase)
            || lowerHost.EndsWith(".visualstudio.com", StringComparison.OrdinalIgnoreCase))
            return TryBuildAzurePath(path, sha, out browserUrl);

        var escapedPath = EscapePath(path);
        var commitSegment = lowerHost.Contains("bitbucket", StringComparison.OrdinalIgnoreCase)
            ? "commits"
            : "commit";
        browserUrl = $"https://{host}/{escapedPath}/{commitSegment}/{Uri.EscapeDataString(sha)}";
        return true;
    }

    private static bool TryParseRemote(string remoteUrl, out string host, out string path)
    {
        host = string.Empty;
        path = string.Empty;

        if (Uri.TryCreate(remoteUrl, UriKind.Absolute, out var uri))
        {
            host = uri.Host;
            path = uri.AbsolutePath.Trim('/');
            return !string.IsNullOrWhiteSpace(host) && !string.IsNullOrWhiteSpace(path);
        }

        var scp = ScpLikeRemoteRegex().Match(remoteUrl);
        if (!scp.Success)
            return false;

        host = scp.Groups["host"].Value;
        path = scp.Groups["path"].Value;
        return true;
    }

    private static string NormalizePath(string path)
    {
        var normalized = path.Replace('\\', '/').Trim('/');
        if (normalized.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^4];
        return normalized;
    }

    private static string EscapePath(string path) =>
        string.Join("/", path
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString));

    private static bool TryBuildAzureSsh(string remoteUrl, string sha, out string browserUrl)
    {
        browserUrl = string.Empty;
        var match = AzureSshRemoteRegex().Match(remoteUrl);
        if (!match.Success)
            return false;

        browserUrl =
            $"https://dev.azure.com/{Uri.EscapeDataString(match.Groups["org"].Value)}" +
            $"/{Uri.EscapeDataString(match.Groups["project"].Value)}" +
            $"/_git/{Uri.EscapeDataString(TrimGitSuffix(match.Groups["repo"].Value))}" +
            $"/commit/{Uri.EscapeDataString(sha)}";
        return true;
    }

    private static bool TryBuildAzurePath(string path, string sha, out string browserUrl)
    {
        browserUrl = string.Empty;
        var normalized = NormalizePath(path);
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);

        var gitIndex = Array.FindIndex(parts, p => p.Equals("_git", StringComparison.OrdinalIgnoreCase));
        if (gitIndex >= 1 && gitIndex + 1 < parts.Length)
        {
            var prefix = string.Join("/", parts.Take(gitIndex).Select(Uri.EscapeDataString));
            var repo = Uri.EscapeDataString(TrimGitSuffix(parts[gitIndex + 1]));
            browserUrl = $"https://dev.azure.com/{prefix}/_git/{repo}/commit/{Uri.EscapeDataString(sha)}";
            return true;
        }

        if (parts.Length >= 3)
        {
            browserUrl =
                $"https://dev.azure.com/{Uri.EscapeDataString(parts[0])}" +
                $"/{Uri.EscapeDataString(parts[1])}" +
                $"/_git/{Uri.EscapeDataString(TrimGitSuffix(parts[2]))}" +
                $"/commit/{Uri.EscapeDataString(sha)}";
            return true;
        }

        return false;
    }

    private static string TrimGitSuffix(string value) =>
        value.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? value[..^4] : value;

    [GeneratedRegex(@"^(?:[^@]+@)?(?<host>[^:]+):(?<path>.+)$")]
    private static partial Regex ScpLikeRemoteRegex();

    [GeneratedRegex(@"^(?:[^@]+@)?ssh\.dev\.azure\.com:v3/(?<org>[^/]+)/(?<project>[^/]+)/(?<repo>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex AzureSshRemoteRegex();
}
