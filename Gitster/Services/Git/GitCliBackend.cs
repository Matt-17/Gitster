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
            ? GitCapabilities.FixupAutosquash | GitCapabilities.InteractiveRebase | GitCapabilities.Worktrees
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

        // Capture the real pre-operation HEAD so we can always get back to it.
        var originalHead = await GetHeadAsync();

        // 1 — Create fixup! commit (no editor needed with --fixup)
        var commit = await GitCli.RunAsync(RepositoryPath, $"commit --fixup={targetSha}");
        if (!commit.Success)
            throw new InvalidOperationException(
                $"Failed to create fixup commit:\n{commit.Output}\n\n" +
                "Make sure you have staged changes before using fixup.");

        // 2 — Non-interactive autosquash rebase.
        //     The *sequence* editor must accept the autosquashed todo unchanged.
        //     Git runs the editor through its bundled `sh`, where `true` is a builtin
        //     no-op — far more reliable on Windows than `cmd /c exit 0` (which breaks
        //     because the todo path is appended as an argument).
        var rebase = await GitCli.RunAsync(RepositoryPath,
            $"rebase --autosquash --interactive {sha7}^",
            new Dictionary<string, string>
            {
                ["GIT_SEQUENCE_EDITOR"] = "true",
                ["GIT_EDITOR"]          = "true",
                ["GIT_TERMINAL_PROMPT"] = "0",
            });

        if (!rebase.Success)
        {
            // Abort the rebase, then drop the fixup! commit while keeping its changes
            // staged — restoring the user to exactly their pre-fixup state.
            await AbortRebaseAndRestoreAsync(originalHead, keepStaged: true);
            throw new InvalidOperationException(
                $"Autosquash rebase hit a conflict and was aborted — your changes are " +
                $"staged again and history is unchanged. Resolve the overlap manually.\n\n{rebase.Output}");
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

        var originalHead = await GetHeadAsync();

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
            edPath = GitCli.WriteTempBat(
                $"@copy /y \"{msgPath}\" \"%1\" >nul",
                "ed");

            var rebase = await GitCli.RunAsync(RepositoryPath,
                $"rebase --interactive {sha7}^",
                new Dictionary<string, string>
                {
                    // Git runs editors through `sh`; paths must be sh-safe (forward
                    // slashes, quoted to survive spaces) or the rebase fails.
                    ["GIT_SEQUENCE_EDITOR"] = GitCli.ToEditorArg(seqEdPath),
                    ["GIT_EDITOR"]          = GitCli.ToEditorArg(edPath),
                    ["GIT_TERMINAL_PROMPT"] = "0",
                });

            if (!rebase.Success)
            {
                await AbortRebaseAndRestoreAsync(originalHead, keepStaged: false);
                throw new InvalidOperationException(
                    $"Reword rebase failed and was aborted — history is unchanged.\n\n{rebase.Output}");
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

        var originalHead = await GetHeadAsync();

        string? msgPath   = null;
        string? seqEdPath = null;
        string? edPath    = null;

        try
        {
            // Build the sequence-editor script as a SINGLE-line PowerShell command.
            // A multi-line .cmd does NOT work here: `@` only suppresses echo on the
            // first line, and cmd treats each subsequent line as its own command, so the
            // PowerShell body would never run. For every SHA after the first, rewrite
            // "pick <sha7>" → "squash <sha7>" so they fold into the oldest.
            var replaces = new System.Text.StringBuilder();
            for (int i = 1; i < orderedOldestFirst.Count; i++)
            {
                var s7 = orderedOldestFirst[i].Length >= 7 ? orderedOldestFirst[i][..7] : orderedOldestFirst[i];
                replaces.Append($"$c = $c -replace 'pick {s7}', 'squash {s7}'; ");
            }
            var seqScript =
                "@powershell -NoProfile -Command \"& { $f = '%1'; $c = Get-Content -Raw $f; " +
                replaces +
                "[IO.File]::WriteAllText($f,$c) }\"";

            msgPath   = GitCli.WriteTempMsg(combinedMessage.TrimEnd());
            seqEdPath = GitCli.WriteTempBat(seqScript, "seqed");
            edPath    = GitCli.WriteTempBat($"@copy /y \"{msgPath}\" \"%1\" >nul", "ed");

            var rebase = await GitCli.RunAsync(RepositoryPath,
                $"rebase --interactive {oldest7}^",
                new Dictionary<string, string>
                {
                    ["GIT_SEQUENCE_EDITOR"] = GitCli.ToEditorArg(seqEdPath),
                    ["GIT_EDITOR"]          = GitCli.ToEditorArg(edPath),
                    ["GIT_TERMINAL_PROMPT"] = "0",
                });

            if (!rebase.Success)
            {
                await AbortRebaseAndRestoreAsync(originalHead, keepStaged: false);
                throw new InvalidOperationException(
                    $"Squash rebase hit a conflict and was aborted — history is unchanged.\n\n{rebase.Output}");
            }

            // Apply date override if requested — set both author and committer date,
            // matching the libgit2 amend convention used elsewhere in Gitster.
            if (overrideDate.HasValue)
            {
                var isoDate = overrideDate.Value.ToString("yyyy-MM-ddTHH:mm:sszzz");
                var amend   = await GitCli.RunAsync(RepositoryPath,
                    $"commit --amend --no-edit --date \"{isoDate}\"",
                    new Dictionary<string, string>
                    {
                        ["GIT_COMMITTER_DATE"] = isoDate,
                        ["GIT_TERMINAL_PROMPT"] = "0",
                    });
                if (!amend.Success)
                    throw new InvalidOperationException(
                        $"Squash succeeded but date override failed:\n{amend.Output}");
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
    public IAsyncEnumerable<CommitInfo> EnumerateCommitsAsync(Gitster.ViewModels.CommitFilter? filter = null, CancellationToken ct = default) => throw new NotSupportedException("Route through HybridGitBackend.");
    public Task<RemoteSets> ComputeRemoteSetsAsync(CancellationToken ct = default) => NS<RemoteSets>();
    public Task<CommitDetails> GetCommitAsync(string sha)       => NS<CommitDetails>();
    public Task<CommitDiff> GetCommitDiffAsync(string sha, CancellationToken ct = default) => NS<CommitDiff>();
    public Task<WorkingTreeStatus> GetWorkingTreeStatusAsync()  => NS<WorkingTreeStatus>();
    public Task StageAsync(IEnumerable<string> paths)           => NSVoid();
    public Task UnstageAsync(IEnumerable<string> paths)         => NSVoid();
    public Task StageAllAsync()                                 => NSVoid();
    public Task<string> CommitAsync(CommitRequest request)      => NS<string>();
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
    public Task<bool> AreCommitsContiguousAsync(IReadOnlyList<string> shas)    => NS<bool>();

    // Branch ops / commit-to-branch / snapshot are libgit2 — never routed here.
    public Task<IReadOnlyList<BranchListItem>> GetBranchListAsync()            => NS<IReadOnlyList<BranchListItem>>();
    public Task CheckoutBranchAsync(string branchName)                        => NSVoid();
    public Task<string> CreateBranchAsync(string name, string startPointSha)  => NS<string>();
    public Task DeleteBranchAsync(string name, bool force)                    => NSVoid();
    public Task RenameBranchAsync(string oldName, string newName)             => NSVoid();
    public Task<string> CommitToBranchAsync(CommitToBranchRequest request)    => NS<string>();
    public Task<string> CreateSnapshotBranchAsync(string branchName, bool includeUncommitted) => NS<string>();

    // Reword for HEAD is NOT routed here — HybridGitBackend sends it to LibGit2Backend
    public Task RewordCommitHeadAsync(string newMessage) => NSVoid();

    // ── Phase 3: Worktrees (Step D) ─────────────────────────────────────────

    public async Task<IReadOnlyList<WorktreeInfo>> GetWorktreesAsync()
    {
        EnsurePath();
        EnsureCli();

        var r = await GitCli.RunAsync(RepositoryPath, "worktree list --porcelain");
        if (!r.Success)
            throw new InvalidOperationException($"Could not list worktrees:\n{r.Output}");

        return ParseWorktreePorcelain(r.Stdout, RepositoryPath!);
    }

    /// <summary>Parses the output of <c>git worktree list --porcelain</c>.</summary>
    public static IReadOnlyList<WorktreeInfo> ParseWorktreePorcelain(string porcelain, string openPath)
    {
        var result = new List<WorktreeInfo>();

        string? path = null, branch = null, head = null;
        bool locked = false, prunable = false, bare = false, detached = false;
        bool first = true;

        void Flush()
        {
            if (path == null) return;
            var normalizedPath = path.Replace('/', System.IO.Path.DirectorySeparatorChar)
                                     .TrimEnd(System.IO.Path.DirectorySeparatorChar);
            var branchName = detached
                ? (head != null ? $"detached @ {head[..Math.Min(7, head.Length)]}" : "(detached)")
                : (branch ?? string.Empty);
            var isCurrent = string.Equals(
                normalizedPath.TrimEnd(System.IO.Path.DirectorySeparatorChar),
                openPath.TrimEnd(System.IO.Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
            result.Add(new WorktreeInfo(
                Path:       normalizedPath,
                BranchName: bare ? "(bare)" : branchName,
                HeadSha:    head ?? string.Empty,
                IsMain:     first,
                IsLocked:   locked,
                IsPrunable: prunable,
                IsCurrent:  isCurrent));
            first = false;
        }

        foreach (var raw in porcelain.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0)
            {
                Flush();
                path = branch = head = null;
                locked = prunable = bare = detached = false;
                continue;
            }

            if (line.StartsWith("worktree ", StringComparison.Ordinal))
                path = line["worktree ".Length..].Trim();
            else if (line.StartsWith("HEAD ", StringComparison.Ordinal))
                head = line["HEAD ".Length..].Trim();
            else if (line.StartsWith("branch ", StringComparison.Ordinal))
            {
                var refName = line["branch ".Length..].Trim();
                branch = refName.StartsWith("refs/heads/", StringComparison.Ordinal)
                    ? refName["refs/heads/".Length..]
                    : refName;
            }
            else if (line.Equals("bare", StringComparison.Ordinal)) bare = true;
            else if (line.Equals("detached", StringComparison.Ordinal)) detached = true;
            else if (line.StartsWith("locked", StringComparison.Ordinal)) locked = true;
            else if (line.StartsWith("prunable", StringComparison.Ordinal)) prunable = true;
        }
        Flush();

        return result;
    }

    public async Task<string> AddWorktreeAsync(string path, string branchName, bool createBranch)
    {
        EnsurePath();
        EnsureCli();

        var quotedPath = Quote(path);
        var args = createBranch
            ? $"worktree add -b {branchName} {quotedPath}"
            : $"worktree add {quotedPath} {branchName}";

        var r = await GitCli.RunAsync(RepositoryPath, args);
        if (!r.Success)
            throw new InvalidOperationException($"Could not add worktree:\n{r.Output}");

        HeadChanged?.Invoke(this, EventArgs.Empty);
        return path;
    }

    public async Task RemoveWorktreeAsync(string path, bool force)
    {
        EnsurePath();
        EnsureCli();

        var forceFlag = force ? "--force " : string.Empty;
        var r = await GitCli.RunAsync(RepositoryPath, $"worktree remove {forceFlag}{Quote(path)}");
        if (!r.Success)
            throw new InvalidOperationException($"Could not remove worktree:\n{r.Output}");

        HeadChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task PruneWorktreesAsync()
    {
        EnsurePath();
        EnsureCli();

        var r = await GitCli.RunAsync(RepositoryPath, "worktree prune");
        if (!r.Success)
            throw new InvalidOperationException($"Could not prune worktrees:\n{r.Output}");
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

    // ── Rebase safety helpers ───────────────────────────────────────────────

    private async Task<string> GetHeadAsync()
    {
        var r = await GitCli.RunAsync(RepositoryPath, "rev-parse HEAD");
        if (!r.Success)
            throw new InvalidOperationException($"Could not read HEAD:\n{r.Output}");
        return r.Stdout.Trim();
    }

    /// <summary>
    /// Aborts an in-progress rebase and guarantees the repo is back at
    /// <paramref name="originalHead"/>.  When <paramref name="keepStaged"/> is true the
    /// restore uses a soft reset so any commit created before the rebase (e.g. the
    /// <c>fixup!</c> commit) is undone with its changes kept staged; otherwise a hard
    /// reset is used as a fallback if the abort did not fully restore HEAD.
    /// </summary>
    private async Task AbortRebaseAndRestoreAsync(string originalHead, bool keepStaged)
    {
        // Best-effort abort; ignore failure (there may be no rebase in progress if the
        // rebase failed before starting). We verify the end state regardless.
        await GitCli.RunAsync(RepositoryPath, "rebase --abort");

        var head = (await GitCli.RunAsync(RepositoryPath, "rev-parse HEAD")).Stdout.Trim();
        if (!head.Equals(originalHead, StringComparison.OrdinalIgnoreCase))
        {
            var mode = keepStaged ? "--soft" : "--hard";
            await GitCli.RunAsync(RepositoryPath, $"reset {mode} {originalHead}");
        }
    }

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
