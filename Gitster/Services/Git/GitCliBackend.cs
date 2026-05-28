using Gitster.Models;

namespace Gitster.Services.Git;

/// <summary>
/// Implements only the rebase-class operations that LibGit2Sharp cannot do.
/// Everything else throws <see cref="NotSupportedException"/> — callers go
/// through <see cref="HybridGitBackend"/> which routes accordingly.
/// </summary>
public sealed class GitCliBackend : IGitBackend
{
    public string? RepositoryPath { get; private set; }

    public event EventHandler? HeadChanged;

    public GitCapabilities Capabilities =>
        GitCli.IsAvailable
            ? GitCapabilities.FixupAutosquash | GitCapabilities.InteractiveRebase
            : GitCapabilities.None;

    public Task OpenAsync(string path)
    {
        RepositoryPath = path;
        return Task.CompletedTask;
    }

    // ── Fixup (Step F) ─────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <c>fixup!</c> commit from staged changes and immediately
    /// squashes it into <paramref name="targetSha"/> via non-interactive autosquash.
    /// </summary>
    public async Task FixupIntoCommitAsync(string targetSha)
    {
        EnsurePath();
        EnsureCli();

        var sha7 = targetSha.Length >= 7 ? targetSha[..7] : targetSha;

        // 1 — Create fixup! commit (no editor needed with --fixup)
        var commit = await GitCli.RunAsync(RepositoryPath, $"commit --fixup={targetSha}");
        if (!commit.Success)
            throw new InvalidOperationException(
                $"Failed to create fixup commit:\n{commit.Stderr}\n\n" +
                "Make sure you have staged changes before using fixup.");

        // 2 — Non-interactive autosquash rebase
        //     "cmd /c exit 0" is a Windows no-op that accepts the todo unchanged.
        var rebase = await GitCli.RunAsync(RepositoryPath,
            $"-c sequence.editor=\"cmd /c exit 0\" rebase --autosquash --interactive {sha7}^",
            new Dictionary<string, string>
            {
                ["GIT_EDITOR"]          = "cmd /c exit 0",
                ["GIT_TERMINAL_PROMPT"] = "0",
            });

        if (!rebase.Success)
        {
            // Abort to leave the repo clean
            await GitCli.RunAsync(RepositoryPath, "rebase --abort");
            throw new InvalidOperationException(
                $"Autosquash rebase failed and was aborted:\n{rebase.Stderr}");
        }

        HeadChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Reword (Step G) ───────────────────────────────────────────────────

    /// <summary>
    /// Rewords an older commit's message via a scripted interactive rebase.
    /// For HEAD rewording use <see cref="LibGit2Backend"/> directly.
    /// </summary>
    public async Task RewordCommitAsync(string sha, string newMessage)
    {
        EnsurePath();
        EnsureCli();

        var sha7 = sha.Length >= 7 ? sha[..7] : sha;

        string? msgPath   = null;
        string? seqEdPath = null;
        string? edPath    = null;

        try
        {
            // Sequence editor: change "pick <sha7>" → "reword <sha7>"
            msgPath   = GitCli.WriteTempMsg(newMessage.TrimEnd());
            seqEdPath = GitCli.WriteTempBat(
                $"@powershell -NoProfile -Command \"& {{ $f = '%1'; $c = Get-Content -Raw $f;" +
                $" $c = $c -replace 'pick {sha7}', 'reword {sha7}'; [IO.File]::WriteAllText($f,$c) }}\"",
                "seqed");

            // Message editor: copy our new message into the git editor file
            var escapedMsgPath = msgPath.Replace("\\", "\\\\");
            edPath = GitCli.WriteTempBat(
                $"@copy /y \"{msgPath}\" \"%1\" >nul",
                "ed");

            var rebase = await GitCli.RunAsync(RepositoryPath,
                $"rebase --interactive {sha7}^",
                new Dictionary<string, string>
                {
                    ["GIT_SEQUENCE_EDITOR"] = seqEdPath,
                    ["GIT_EDITOR"]          = edPath,
                    ["GIT_TERMINAL_PROMPT"] = "0",
                });

            if (!rebase.Success)
            {
                await GitCli.RunAsync(RepositoryPath, "rebase --abort");
                throw new InvalidOperationException(
                    $"Reword rebase failed and was aborted:\n{rebase.Stderr}");
            }

            HeadChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            GitCli.CleanupTemp(msgPath, seqEdPath, edPath);
        }
    }

    // ── Squash — non-HEAD path (Step H) ───────────────────────────────────

    /// <summary>
    /// Squashes <paramref name="shas"/> (contiguous, NOT including HEAD) via
    /// interactive rebase.  Pass shas newest-first (same order as commit list).
    /// </summary>
    public async Task SquashCommitsAsync(
        IReadOnlyList<string> shas,
        string combinedMessage,
        DateTimeOffset? overrideDate)
    {
        EnsurePath();
        EnsureCli();

        // shas are newest-first; oldest is last
        var orderedOldestFirst = shas.Reverse().ToList();
        var oldest7 = (orderedOldestFirst[0].Length >= 7
            ? orderedOldestFirst[0][..7]
            : orderedOldestFirst[0]);

        string? msgPath   = null;
        string? seqEdPath = null;
        string? edPath    = null;

        try
        {
            // Build the sequence-editor script:
            // For each SHA after the first, change "pick <sha7>" → "squash <sha7>"
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("@powershell -NoProfile -Command \"& {");
            sb.AppendLine("  $f = '%1'");
            sb.AppendLine("  $c = Get-Content -Raw $f");
            for (int i = 1; i < orderedOldestFirst.Count; i++)
            {
                var s7 = orderedOldestFirst[i].Length >= 7 ? orderedOldestFirst[i][..7] : orderedOldestFirst[i];
                sb.AppendLine($"  $c = $c -replace 'pick {s7}', 'squash {s7}'");
            }
            sb.AppendLine("  [IO.File]::WriteAllText($f, $c)");
            sb.AppendLine("}\"");

            msgPath   = GitCli.WriteTempMsg(combinedMessage.TrimEnd());
            seqEdPath = GitCli.WriteTempBat(sb.ToString(), "seqed");
            edPath    = GitCli.WriteTempBat($"@copy /y \"{msgPath}\" \"%1\" >nul", "ed");

            var rebase = await GitCli.RunAsync(RepositoryPath,
                $"rebase --interactive {oldest7}^",
                new Dictionary<string, string>
                {
                    ["GIT_SEQUENCE_EDITOR"] = seqEdPath,
                    ["GIT_EDITOR"]          = edPath,
                    ["GIT_TERMINAL_PROMPT"] = "0",
                });

            if (!rebase.Success)
            {
                await GitCli.RunAsync(RepositoryPath, "rebase --abort");
                throw new InvalidOperationException(
                    $"Squash rebase failed and was aborted:\n{rebase.Stderr}");
            }

            // Apply date override if requested
            if (overrideDate.HasValue)
            {
                var dt      = overrideDate.Value.LocalDateTime;
                var isoDate = overrideDate.Value.ToString("yyyy-MM-ddTHH:mm:ss zzz");
                var amend   = await GitCli.RunAsync(RepositoryPath,
                    $"commit --amend --no-edit --date \"{isoDate}\"",
                    new Dictionary<string, string>
                    {
                        ["GIT_COMMITTER_DATE"] = isoDate,
                        ["GIT_TERMINAL_PROMPT"] = "0",
                    });
                if (!amend.Success)
                    throw new InvalidOperationException(
                        $"Squash succeeded but date override failed:\n{amend.Stderr}");
            }

            HeadChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            GitCli.CleanupTemp(msgPath, seqEdPath, edPath);
        }
    }

    // ── Unsupported pass-through stubs ────────────────────────────────────
    // These are here only to satisfy the interface; HybridGitBackend never
    // routes them here.

    public Task<WorkingTreeState> GetWorkingTreeStateAsync()     => NS<WorkingTreeState>();
    public Task<BranchInfo> GetCurrentBranchAsync()             => NS<BranchInfo>();
    public Task<IReadOnlyList<CommitInfo>> GetCommitsAsync(Gitster.ViewModels.CommitFilter? filter = null) => NS<IReadOnlyList<CommitInfo>>();
    public Task<CommitDetails> GetCommitAsync(string sha)       => NS<CommitDetails>();
    public Task<string> AmendAsync(AmendRequest request)        => NS<string>();
    public Task AmendAuthorAsync(AmendAuthorRequest request)    => NSVoid();
    public Task RewriteCommitsAsync(IEnumerable<CommitRewrite> rewrites) => NSVoid();
    public Task FetchAsync(string remoteName = "origin")        => NSVoid();
    public Task PullAsync(string remoteName = "origin")         => NSVoid();
    public Task PushAsync(string remoteName = "origin", bool forceWithLease = false) => NSVoid();
    public Task<string> GetReflogSelectorForHeadAsync()         => NS<string>();
    public Task ResetHardAsync(string targetReference)          => NSVoid();
    public Task<string> GetHeadShaAsync()                       => NS<string>();
    public Task<string> ResolveRefAsync(string refSpec)         => NS<string>();
    public Task<IReadOnlyList<CommitInfo>> GetCommitsBetweenAsync(string a, string b) => NS<IReadOnlyList<CommitInfo>>();
    public Task<bool> CommitExistsAsync(string sha)             => NS<bool>();
    public Task CherryPickAsync(string sha)                     => NSVoid();
    public Task<Dictionary<string, string>> GetAllRefsAsync()   => NS<Dictionary<string, string>>();
    public Task<int> GetStashCountAsync()                       => NS<int>();
    public Task<IReadOnlyList<StashInfo>> GetStashesAsync()     => NS<IReadOnlyList<StashInfo>>();
    public Task<string> GetStashDiffAsync(int stashIndex)       => NS<string>();
    public Task ApplyStashAsync(int stashIndex, bool reinstateIndex = true) => NSVoid();
    public Task PopStashAsync(int stashIndex, bool reinstateIndex = true)   => NSVoid();
    public Task DropStashAsync(int stashIndex)                  => NSVoid();
    public Task<string> CreateStashAsync(string message, bool includeUntracked = true) => NS<string>();
    public Task<string> ConvertStashToBranchAsync(int stashIndex, string branchName)   => NS<string>();
    public Task<IReadOnlyList<BranchSummary>> GetBranchesAsync()               => NS<IReadOnlyList<BranchSummary>>();
    public Task<IReadOnlyList<CommitInfo>> GetCommitsForRefAsync(string refName, int maxCount = 200) => NS<IReadOnlyList<CommitInfo>>();

    // Reword for HEAD is NOT routed here — HybridGitBackend sends it to LibGit2Backend
    public Task RewordCommitHeadAsync(string newMessage) => NSVoid();

    // ── Helpers ────────────────────────────────────────────────────────────

    private void EnsurePath()
    {
        if (string.IsNullOrWhiteSpace(RepositoryPath))
            throw new InvalidOperationException("Repository is not opened.");
    }

    private static void EnsureCli()
    {
        if (!GitCli.IsAvailable)
            throw new InvalidOperationException(
                "This operation requires the Git command-line tool. " +
                "Install Git for Windows and restart Gitster.");
    }

    private static Task<T> NS<T>() =>
        throw new NotSupportedException("Route this call through HybridGitBackend.");

    private static Task NSVoid() =>
        throw new NotSupportedException("Route this call through HybridGitBackend.");
}
