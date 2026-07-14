using System.IO;

using Gitster.Core.Git;

namespace Gitster.ApplicationLayer.Features;

public sealed class GitFeatureService
{
    public async Task<IReadOnlyList<ReflogEntry>> GetReflogAsync(
        string repoPath,
        int maxCount = 500,
        CancellationToken ct = default)
    {
        var result = await GitCli.RunAsync(
            repoPath,
            ["log", "-g", $"--max-count={maxCount}", "--format=%gd%x09%H%x09%gs%x09%aI"],
            ct: ct);

        if (!result.Success)
            throw new InvalidOperationException($"Could not read reflog:\n{result.Output}");

        return result.Stdout
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseReflogLine)
            .ToList();
    }

    public async Task<Dictionary<string, CommitSigningStatus>> GetSigningStatusesAsync(
        string repoPath,
        IReadOnlyList<string> shas,
        CancellationToken ct = default)
    {
        if (shas.Count == 0)
            return new Dictionary<string, CommitSigningStatus>(StringComparer.OrdinalIgnoreCase);

        var args = new List<string> { "log", "--no-walk", "--format=%H%x09%G?" };
        args.AddRange(shas);
        var result = await GitCli.RunAsync(repoPath, args, ct: ct);
        if (!result.Success)
            throw new InvalidOperationException($"Could not verify commit signatures:\n{result.Output}");

        return result.Stdout
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(
                parts => parts[0],
                parts => CommitSigningStatusParser.Parse(parts[1]),
                StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<SubmoduleStatus>> GetSubmoduleStatusAsync(
        string repoPath,
        CancellationToken ct = default)
    {
        if (!File.Exists(Path.Combine(repoPath, ".gitmodules")))
            return [];

        var result = await GitCli.RunAsync(repoPath, ["submodule", "status", "--recursive"], ct: ct);
        if (!result.Success)
            throw new InvalidOperationException($"Could not read submodule status:\n{result.Output}");

        return SubmoduleStatusParser.Parse(result.Stdout);
    }

    private static ReflogEntry ParseReflogLine(string line)
    {
        var parts = line.Split('\t');
        var subject = parts.ElementAtOrDefault(2) ?? string.Empty;
        var action = subject;
        var message = string.Empty;
        var separator = subject.IndexOf(": ", StringComparison.Ordinal);
        if (separator >= 0)
        {
            action = subject[..separator];
            message = subject[(separator + 2)..];
        }

        return new ReflogEntry(
            parts.ElementAtOrDefault(0) ?? string.Empty,
            parts.ElementAtOrDefault(1) ?? string.Empty,
            action,
            message,
            DateTimeOffset.TryParse(parts.ElementAtOrDefault(3), out var date) ? date : null);
    }
}
