using System.IO;
using System.Text;
using System.Windows;

using Gitster.Core.Models;
using Gitster.Core.Git;

using Microsoft.Win32;

using Gitster.Core;

namespace Gitster.Services;

public sealed class SourceArchiveService
{
    private const string ZipFilter = "Zip archive (*.zip)|*.zip|All files (*.*)|*.*";

    private readonly IGitBackend _git;
    private readonly IWindowService _windowService;
    private readonly OperationFeedbackService _feedback;

    public SourceArchiveService(
        IGitBackend git,
        IWindowService windowService,
        OperationFeedbackService feedback)
    {
        _git = git;
        _windowService = windowService;
        _feedback = feedback;
    }

    public async Task ArchiveRefAsync(string treeish, string targetLabel, string? knownSha)
    {
        if (string.IsNullOrWhiteSpace(_git.RepositoryPath))
            return;

        if (!_git.Capabilities.HasFlag(GitCapabilities.SourceArchive))
        {
            _windowService.Warning(
                "Source archives require the Git command-line tool.\nInstall Git for Windows and restart Gitster.",
                "Git CLI required");
            return;
        }

        try
        {
            var repositoryPath = NormalizeDirectoryPath(_git.RepositoryPath);
            var repositoryName = Path.GetFileName(repositoryPath);
            var shortSha = ShortSha(knownSha ?? treeish);
            var safeTarget = SanitizeFileNamePart(targetLabel);
            var defaultBaseName = $"{repositoryName}-{safeTarget}-{shortSha}";

            var dialog = new SaveFileDialog
            {
                Title = $"Archive {targetLabel}",
                InitialDirectory = Directory.GetParent(repositoryPath)?.FullName ?? repositoryPath,
                FileName = defaultBaseName + ".zip",
                DefaultExt = ".zip",
                AddExtension = true,
                OverwritePrompt = true,
                Filter = ZipFilter,
            };

            if (_windowService.ShowDialog(dialog) != true)
                return;

            if (!await ConfirmDirtyWorkingTreeAsync())
                return;

            if (IsPathInsideDirectory(dialog.FileName, repositoryPath) && !ConfirmInsideRepository())
                return;

            var prefix = Path.GetFileNameWithoutExtension(dialog.FileName);
            await _feedback.RunAsync(
                "Archive",
                () => _git.ArchiveSourceZipAsync(new ArchiveRequest(treeish, dialog.FileName, prefix)),
                result => $"{Path.GetFileName(result.OutputPath)} ({FormatSize(result.SizeBytes)})");
        }
        catch (Exception ex)
        {
            _windowService.Error($"Archive failed:\n{ex.Message}", "Gitster");
        }
    }

    private async Task<bool> ConfirmDirtyWorkingTreeAsync()
    {
        WorkingTreeState state;
        try
        {
            state = await _git.GetWorkingTreeStateAsync();
        }
        catch
        {
            return true;
        }

        if (state is WorkingTreeState.Clean)
            return true;

        var detail = state switch
        {
            WorkingTreeState.Dirty dirty =>
                $"Modified: {dirty.Modified}, staged: {dirty.Staged}, untracked: {dirty.Untracked}.",
            WorkingTreeState.Merging merging =>
                $"Merge in progress from {merging.FromBranch}.",
            WorkingTreeState.Rebasing rebasing =>
                $"Rebase in progress ({rebasing.CurrentStep}/{rebasing.TotalSteps}).",
            WorkingTreeState.CherryPicking cherryPicking =>
                $"Cherry-pick in progress at {ShortSha(cherryPicking.Sha)}.",
            _ => "The working tree is not clean.",
        };

        var result = _windowService.ShowMessage(
            "Git archive exports only committed, tracked files from the selected ref.\n\n" +
            $"{detail}\n\n" +
            "Staged, unstaged, and untracked files will not be included.\n\nContinue?",
            "Uncommitted changes not included",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        return result == MessageBoxResult.Yes;
    }

    private bool ConfirmInsideRepository()
    {
        var result = _windowService.ShowMessage(
            "The selected ZIP path is inside the repository working tree.\n\n" +
            "After export, Git may show the archive as an untracked or modified file.\n\nContinue?",
            "Archive inside repository",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        return result == MessageBoxResult.Yes;
    }

    private static bool IsPathInsideDirectory(string candidatePath, string directoryPath)
    {
        var directory = NormalizeDirectoryPath(directoryPath) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(candidatePath);
        return candidate.StartsWith(directory, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDirectoryPath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string ShortSha(string value) => Gitster.Core.Git.GitSha.Short(value);

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";

        var kib = bytes / 1024d;
        if (kib < 1024)
            return $"{kib:0.#} KB";

        return $"{kib / 1024d:0.#} MB";
    }

    private static string SanitizeFileNamePart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().Append('/').Append('\\').ToHashSet();
        var builder = new StringBuilder(value.Length);

        foreach (var ch in value.Trim())
            builder.Append(invalid.Contains(ch) ? '-' : ch);

        var sanitized = builder.ToString().Trim(' ', '.');
        return sanitized.Length == 0 ? "archive" : sanitized;
    }
}
