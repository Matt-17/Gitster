using Gitster.Core.Git;
using Gitster.Views;

using Gitster.Core;

namespace Gitster.Services.Features;

public enum ConflictGuidanceAction
{
    Close,
    Retry,
    OpenMergeTool,
}

public sealed record ConflictGuidance(
    string OperationName,
    string StateSummary,
    string RawMessage,
    IReadOnlyList<string> Files,
    bool RepositoryHalted,
    bool CanOpenMergeTool);

public static class ConflictGuidanceService
{
    public static bool LooksLikeConflict(Exception ex) =>
        ex is GitConflictException
        // Message heuristic remains as fallback for CLI-origin errors (raw git output).
        || ex.Message.Contains("conflict", StringComparison.OrdinalIgnoreCase);

    public static async Task<ConflictGuidance> BuildAsync(
        IGitBackend git,
        string operationName,
        Exception ex)
    {
        var files = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var repositoryHalted = ex is GitConflictException typed
            ? typed.RepositoryHalted
            : !MessageSaysRestored(ex.Message);

        try
        {
            var status = await git.GetWorkingTreeStatusAsync();
            foreach (var file in status.Staged.Concat(status.Unstaged))
            {
                if (file.Status == WorkingFileStatus.Conflicted)
                    files.Add(file.Path);
            }
        }
        catch
        {
            // If status cannot be read, the Git output is still useful guidance.
        }

        foreach (var file in ParseConflictFiles(ex.Message))
            files.Add(file);

        var state = repositoryHalted
            ? "Gitster stopped because the repository is in a conflict state. Resolve or abort the Git operation before starting another rewrite."
            : "Gitster aborted the operation and restored the repository state. Your history and working tree should be unchanged.";

        return new ConflictGuidance(
            operationName,
            state,
            ex.Message,
            files.ToList(),
            repositoryHalted,
            CanOpenMergeTool: repositoryHalted && git.RepositoryPath is not null);
    }

    public static async Task<bool> ShowIfConflictAsync(
        IWindowService windows,
        IGitBackend git,
        string operationName,
        Exception ex)
    {
        if (!LooksLikeConflict(ex))
            return false;

        var guidance = await BuildAsync(git, operationName, ex);
        var dialog = new ConflictGuidanceDialog(guidance);
        var result = windows.ShowDialog(dialog);

        if (result == true && dialog.SelectedAction == ConflictGuidanceAction.OpenMergeTool)
        {
            try
            {
                await OpenMergeToolAsync(git.RepositoryPath);
            }
            catch (Exception launchEx)
            {
                windows.Warning(launchEx.Message, "Merge tool");
            }
        }

        return true;
    }

    public static async Task OpenMergeToolAsync(string? repoPath)
    {
        if (string.IsNullOrWhiteSpace(repoPath))
            throw new InvalidOperationException("No repository is open.");

        var result = await GitCli.RunAsync(repoPath, MergeToolArgs());
        if (!result.Success)
            throw new InvalidOperationException($"Could not launch git mergetool:\n{result.Output}");
    }

    internal static IReadOnlyList<string> MergeToolArgs() => ["mergetool"];

    internal static IReadOnlyList<string> ParseConflictFiles(string message)
    {
        var files = new List<string>();
        foreach (var raw in message.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || !line.Contains("conflict", StringComparison.OrdinalIgnoreCase))
                continue;

            var looksLikeGitConflictPath = line.StartsWith("CONFLICT", StringComparison.Ordinal)
                || line.Contains("conflict in ", StringComparison.OrdinalIgnoreCase);
            if (!looksLikeGitConflictPath)
                continue;

            var marker = " in ";
            var index = line.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index >= 0 && index + marker.Length < line.Length)
            {
                files.Add(line[(index + marker.Length)..].Trim(' ', '.', ':', '\'', '"'));
                continue;
            }

            marker = "CONFLICT";
            index = line.IndexOf(marker, StringComparison.Ordinal);
            if (index >= 0 && line.Contains(':', StringComparison.Ordinal))
                files.Add(line);
        }

        return files;
    }

    private static bool MessageSaysRestored(string message) =>
        message.Contains("aborted", StringComparison.OrdinalIgnoreCase)
        || message.Contains("unchanged", StringComparison.OrdinalIgnoreCase)
        || message.Contains("restored", StringComparison.OrdinalIgnoreCase);
}
