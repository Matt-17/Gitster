using System.IO;

using Gitster.Models;

namespace Gitster.Services.Git;

/// <summary>
/// Implements Git CLI operations, including all server work so authentication
/// stays with the user's configured Git credential flow. Local operations that
/// belong to <see cref="LibGit2Backend"/> throw <see cref="NotSupportedException"/>;
/// callers go through <see cref="HybridGitBackend"/> which routes accordingly.
/// </summary>
public sealed class GitCliBackend : IGitBackend
{
    private static readonly TimeSpan RemoteOperationTimeout = TimeSpan.FromMinutes(10);

    public string? RepositoryPath { get; private set; }

    public event EventHandler? HeadChanged;

    public GitCapabilities Capabilities =>
        GitCli.IsAvailable
            ? GitCapabilities.FixupAutosquash | GitCapabilities.InteractiveRebase | GitCapabilities.Worktrees
              | GitCapabilities.PickaxeSearch | GitCapabilities.DiffRegexSearch
              | GitCapabilities.RangeDiff | GitCapabilities.BlameFollow | GitCapabilities.SourceArchive
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
        await EnsureNotShallowRepositoryAsync("fix up commits");

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

    public async Task FixupCommitIntoCommitAsync(string sourceSha, string targetSha)
    {
        EnsurePath();
        EnsureCli();
        await EnsureNotShallowRepositoryAsync("fix up commits");

        if (string.IsNullOrWhiteSpace(sourceSha))
            throw new ArgumentException("Source commit SHA is required.", nameof(sourceSha));
        if (string.IsNullOrWhiteSpace(targetSha))
            throw new ArgumentException("Target commit SHA is required.", nameof(targetSha));

        var source = await ResolveCommitAsync(sourceSha);
        var target = await ResolveCommitAsync(targetSha);
        if (source.Equals(target, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Choose two different commits for fixup.");

        var sourceOnHead = await GitCli.RunAsync(RepositoryPath, ["merge-base", "--is-ancestor", source, "HEAD"]);
        if (!sourceOnHead.Success)
            throw new InvalidOperationException("The source commit must be on the current branch.");

        var targetBeforeSource = await GitCli.RunAsync(RepositoryPath, ["merge-base", "--is-ancestor", target, source]);
        if (!targetBeforeSource.Success)
            throw new InvalidOperationException("Drop onto an older commit on the same branch.");

        var source7 = ShortSha(source);
        var target7 = ShortSha(target);
        var targetParent = await GitCli.RunAsync(RepositoryPath, ["rev-parse", "--verify", $"{target}^"]);
        var upstream = targetParent.Success ? $"{target7}^" : "--root";
        var originalHead = await GetHeadAsync();
        string? seqEdPath = null;

        try
        {
            seqEdPath = GitCli.WriteTempBat(BuildFixupTodoEditorScript(source7, target7), "fixup-drop-seqed");
            var rebase = await GitCli.RunAsync(
                RepositoryPath,
                $"rebase --interactive {upstream}",
                new Dictionary<string, string>
                {
                    ["GIT_SEQUENCE_EDITOR"] = GitCli.ToEditorArg(seqEdPath),
                    ["GIT_EDITOR"] = "true",
                    ["GIT_TERMINAL_PROMPT"] = "0",
                });

            if (!rebase.Success)
            {
                await AbortRebaseAndRestoreAsync(originalHead, keepStaged: false);
                throw new InvalidOperationException(
                    $"Fixup rebase hit a conflict and was aborted - history is unchanged.\n\n{rebase.Output}");
            }

            HeadChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            GitCli.CleanupTemp(seqEdPath);
        }
    }

    /// <summary>
    /// Rewords an older commit's message via a scripted interactive rebase.
    /// For HEAD rewording use <see cref="LibGit2Backend"/> directly.
    /// </summary>
    public async Task RewordCommitAsync(string sha, string newMessage)
    {
        EnsurePath();
        EnsureCli();
        await EnsureNotShallowRepositoryAsync("reword commits");

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
        await EnsureNotShallowRepositoryAsync("squash commits");

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

    // ── Phase 4: Search & Analysis (Part B) ───────────────────────────────

    private const string LogFormat = "%H%x1f%an%x1f%ae%x1f%aI%x1f%s";

    public async Task<IReadOnlyList<CommitInfo>> PickaxeSearchAsync(string term, string? path, CancellationToken ct = default)
    {
        EnsurePath(); EnsureCli();
        var pathArg = string.IsNullOrWhiteSpace(path) ? string.Empty : $" -- {QuoteArg(path)}";
        var r = await GitCli.RunAsync(RepositoryPath, $"log -S{QuoteArg(term)} --pretty=format:{LogFormat}{pathArg}", null, ct);
        if (!r.Success && string.IsNullOrEmpty(r.Stdout))
            throw new InvalidOperationException($"Pickaxe search failed:\n{r.Output}");
        return ParseLog(r.Stdout);
    }

    public async Task<IReadOnlyList<CommitInfo>> DiffRegexSearchAsync(string pattern, string? path, CancellationToken ct = default)
    {
        EnsurePath(); EnsureCli();
        var pathArg = string.IsNullOrWhiteSpace(path) ? string.Empty : $" -- {QuoteArg(path)}";
        var r = await GitCli.RunAsync(RepositoryPath, $"log -G{QuoteArg(pattern)} --pretty=format:{LogFormat}{pathArg}", null, ct);
        if (!r.Success && string.IsNullOrEmpty(r.Stdout))
            throw new InvalidOperationException($"Diff-regex search failed:\n{r.Output}");
        return ParseLog(r.Stdout);
    }

    public async Task<IReadOnlyList<BlameLine>> BlameAsync(string filePath, bool ignoreWhitespace, bool followMoves, CancellationToken ct = default)
    {
        EnsurePath(); EnsureCli();
        var ws = ignoreWhitespace ? "-w " : string.Empty;
        var moves = followMoves ? "-C -C -C " : string.Empty;
        var r = await GitCli.RunAsync(RepositoryPath, $"blame {ws}{moves}--line-porcelain {QuoteArg(filePath)}", null, ct);
        if (!r.Success)
            throw new InvalidOperationException($"Blame failed:\n{r.Output}");
        return ParseBlamePorcelain(r.Stdout);
    }

    public async Task<IReadOnlyList<RangeDiffEntry>> RangeDiffAsync(string range1, string range2, CancellationToken ct = default)
    {
        EnsurePath(); EnsureCli();
        var r = await GitCli.RunAsync(RepositoryPath, $"range-diff {range1} {range2}", null, ct);
        if (!r.Success)
            throw new InvalidOperationException($"Range-diff failed:\n{r.Output}");
        return ParseRangeDiff(r.Stdout);
    }

    public async Task<string?> GetPriorTipFromReflogAsync()
    {
        EnsurePath(); EnsureCli();
        var r = await GitCli.RunAsync(RepositoryPath, "rev-parse HEAD@{1}");
        return r.Success ? r.Stdout.Trim() : null;
    }

    public Task<CompareResult> CompareRefsAsync(string baseRef, string compareRef, bool threeDot, CancellationToken ct = default)
        => NS<CompareResult>();

    private static List<CommitInfo> ParseLog(string stdout)
    {
        var result = new List<CommitInfo>();
        foreach (var line in stdout.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = line.Split('\x1f');
            if (p.Length < 5) continue;
            var date = DateTimeOffset.TryParse(p[3], out var dt) ? dt.LocalDateTime : DateTime.MinValue;
            var sha = p[0];
            result.Add(new CommitInfo(sha.Length >= 7 ? sha[..7] : sha, p[4], date, p[1], p[2], CommitRemoteState.LocalOnly, sha));
        }
        return result;
    }

    private static List<BlameLine> ParseBlamePorcelain(string stdout)
    {
        var result = new List<BlameLine>();
        string sha = string.Empty, author = string.Empty;
        int lineNum = 0;
        DateTimeOffset when = DateTimeOffset.MinValue;

        foreach (var raw in stdout.Replace("\r\n", "\n").Split('\n'))
        {
            if (raw.Length == 0) continue;
            if (raw[0] == '\t')
            {
                result.Add(new BlameLine(lineNum, sha, author, when, raw[1..]));
                continue;
            }
            if (raw.Length >= 40 && IsHex(raw.AsSpan(0, 40)))
            {
                var parts = raw.Split(' ');
                sha = parts[0];
                if (parts.Length >= 3 && int.TryParse(parts[2], out var n)) lineNum = n;
            }
            else if (raw.StartsWith("author ", StringComparison.Ordinal))
                author = raw["author ".Length..];
            else if (raw.StartsWith("author-time ", StringComparison.Ordinal)
                     && long.TryParse(raw["author-time ".Length..], out var unix))
                when = DateTimeOffset.FromUnixTimeSeconds(unix);
        }
        return result;
    }

    private static bool IsHex(ReadOnlySpan<char> s)
    {
        foreach (var c in s)
            if (!Uri.IsHexDigit(c)) return false;
        return true;
    }

    private static readonly System.Text.RegularExpressions.Regex RangeDiffLine =
        new(@"^\s*(\S+):\s+(\S+)\s+([=!<>])\s+(\S+):\s+(\S+)\s+(.*)$",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    private static List<RangeDiffEntry> ParseRangeDiff(string stdout)
    {
        var result = new List<RangeDiffEntry>();
        foreach (var raw in stdout.Replace("\r\n", "\n").Split('\n'))
        {
            var m = RangeDiffLine.Match(raw);
            if (!m.Success) continue;
            var op = m.Groups[3].Value;
            var leftSha = NullIfDashes(m.Groups[2].Value);
            var rightSha = NullIfDashes(m.Groups[5].Value);
            var status = op switch
            {
                "=" => RangeDiffStatus.Unchanged,
                "!" => RangeDiffStatus.Modified,
                "<" => RangeDiffStatus.Removed,
                ">" => RangeDiffStatus.Added,
                _   => RangeDiffStatus.Modified,
            };
            result.Add(new RangeDiffEntry(status, leftSha, rightSha, m.Groups[6].Value.Trim()));
        }
        return result;
    }

    private static string? NullIfDashes(string s) =>
        s.All(c => c == '-') ? null : s;

    private static string QuoteArg(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

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
    public Task RewriteCommitsAsync(IEnumerable<CommitRewrite> rewrites, string? branchName = null) => NSVoid();
    public Task RemoveFileChangeFromCommitAsync(string sha, string path, string? branchName = null) => NSVoid();

    // Remote server operations. Keep these on the Git CLI path only.
    public async Task FetchAsync(string remoteName = "origin", CancellationToken ct = default)
    {
        EnsurePath();
        EnsureCli();

        var remote = NormalizeRemoteName(remoteName);
        await EnsureRemoteExistsAsync(remote, ct);

        var r = await GitCli.RunAsync(
            RepositoryPath,
            ["fetch", remote],
            NoPromptEnvironment(),
            ct,
            timeout: RemoteOperationTimeout);
        if (!r.Success)
            throw new InvalidOperationException($"Fetch failed:\n{r.Output}");
    }

    public async Task PullAsync(string remoteName = "origin", CancellationToken ct = default)
    {
        EnsurePath();
        EnsureCli();

        var remote = NormalizeRemoteName(remoteName);
        await EnsureRemoteExistsAsync(remote, ct);

        var r = await GitCli.RunAsync(
            RepositoryPath,
            ["pull", remote],
            NoPromptEnvironment(),
            ct,
            timeout: RemoteOperationTimeout);
        if (!r.Success)
            throw new InvalidOperationException($"Pull failed:\n{r.Output}");

        HeadChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Real <c>git push</c> — the only path that can do a true --force-with-lease.</summary>
    public async Task PushAsync(string remoteName = "origin", PushMode mode = PushMode.Normal, CancellationToken ct = default)
    {
        EnsurePath();
        EnsureCli();
        var remote = NormalizeRemoteName(remoteName);
        await EnsureRemoteExistsAsync(remote, ct);

        var flag = mode switch
        {
            PushMode.ForceWithLease => "--force-with-lease ",
            PushMode.Force          => "--force ",
            _                       => string.Empty,
        };

        var r = await GitCli.RunAsync(
            RepositoryPath,
            $"push {flag}{remote} HEAD",
            NoPromptEnvironment(),
            ct,
            timeout: RemoteOperationTimeout);
        if (!r.Success)
            throw new InvalidOperationException($"Push failed:\n{r.Output}");

        var head = await GitCli.RunAsync(RepositoryPath, ["rev-parse", "--verify", "HEAD"], ct: ct);
        if (head.Success)
            await UpdateTrackingRefAfterPushAsync(remote, head.Stdout.Trim(), ct);
    }

    public async Task PushThroughCommitAsync(string commitSha, string remoteName = "origin", CancellationToken ct = default)
    {
        EnsurePath();
        EnsureCli();

        if (string.IsNullOrWhiteSpace(commitSha))
            throw new ArgumentException("Commit SHA is required.", nameof(commitSha));

        var branch = await GitCli.RunAsync(RepositoryPath, ["symbolic-ref", "HEAD"], ct: ct);
        if (!branch.Success)
            throw new InvalidOperationException("Check out a local branch before pushing through a commit.");

        var targetRef = branch.Stdout.Trim();
        var resolved = await GitCli.RunAsync(RepositoryPath, ["rev-parse", "--verify", $"{commitSha}^{{commit}}"], ct: ct);
        if (!resolved.Success)
            throw new InvalidOperationException($"Commit not found: {commitSha}");

        var resolvedSha = resolved.Stdout.Trim();
        var ancestor = await GitCli.RunAsync(RepositoryPath, ["merge-base", "--is-ancestor", resolvedSha, "HEAD"], ct: ct);
        if (!ancestor.Success)
            throw new InvalidOperationException("The selected commit is not on the current branch.");

        var remote = NormalizeRemoteName(remoteName);
        await EnsureRemoteExistsAsync(remote, ct);
        var r = await GitCli.RunAsync(
            RepositoryPath,
            ["push", remote, $"{resolvedSha}:{targetRef}"],
            NoPromptEnvironment(),
            ct,
            timeout: RemoteOperationTimeout);
        if (!r.Success)
            throw new InvalidOperationException($"Push failed:\n{r.Output}");

        await UpdateTrackingRefAfterPushAsync(remote, resolvedSha, ct);
    }

    private async Task UpdateTrackingRefAfterPushAsync(string remoteName, string commitSha, CancellationToken ct = default)
    {
        var upstream = await GitCli.RunAsync(RepositoryPath, ["rev-parse", "--symbolic-full-name", "@{u}"], ct: ct);
        if (!upstream.Success)
            return;

        var upstreamRef = upstream.Stdout.Trim();
        if (!upstreamRef.StartsWith($"refs/remotes/{remoteName}/", StringComparison.Ordinal))
            return;

        await GitCli.RunAsync(RepositoryPath, ["update-ref", upstreamRef, commitSha], ct: ct);
    }

    public async Task<HistoryStitchResult> StitchHistoryAsync(string sourceRef)
    {
        EnsurePath();
        EnsureCli();

        var status = await GitCli.RunAsync(RepositoryPath, ["status", "--porcelain"]);
        if (!status.Success)
            throw new InvalidOperationException($"Could not read working tree status:\n{status.Output}");
        if (!string.IsNullOrWhiteSpace(status.Stdout))
            throw new InvalidOperationException(
                "Cannot stitch history while the working tree has uncommitted changes. Commit or stash them first.");

        var currentBranch = await GitCli.RunAsync(RepositoryPath, ["symbolic-ref", "--short", "HEAD"]);
        if (!currentBranch.Success)
            throw new InvalidOperationException("Check out a local branch before stitching history.");

        var targetBranch = currentBranch.Stdout.Trim();
        if (string.Equals(sourceRef, targetBranch, StringComparison.Ordinal))
            throw new InvalidOperationException("Choose an old source branch, not the current branch.");

        var head = await GitCli.RunAsync(RepositoryPath, ["rev-parse", "--verify", "HEAD^{commit}"]);
        if (!head.Success)
            throw new InvalidOperationException($"Could not resolve HEAD:\n{head.Output}");

        var source = await GitCli.RunAsync(RepositoryPath, ["rev-parse", "--verify", $"{sourceRef}^{{commit}}"]);
        if (!source.Success)
            throw new InvalidOperationException($"Source branch or ref '{sourceRef}' was not found.");

        var ancestor = await GitCli.RunAsync(RepositoryPath, ["merge-base", "--is-ancestor", source.Stdout.Trim(), "HEAD"]);
        if (ancestor.ExitCode == 0)
            throw new InvalidOperationException($"'{sourceRef}' is already reachable from the current branch.");
        if (ancestor.ExitCode != 1)
            throw new InvalidOperationException($"Could not compare source branch ancestry:\n{ancestor.Output}");

        var backupBranch = await BuildAvailableBackupBranchNameAsync(targetBranch, head.Stdout.Trim());
        var backup = await GitCli.RunAsync(RepositoryPath, ["branch", backupBranch, "HEAD"]);
        if (!backup.Success)
            throw new InvalidOperationException($"Could not create backup branch '{backupBranch}':\n{backup.Output}");

        var merge = await GitCli.RunAsync(
            RepositoryPath,
            [
                "merge",
                "--no-ff",
                "-s",
                "ours",
                sourceRef,
                "-m",
                $"Record original history of {sourceRef}",
            ],
            new Dictionary<string, string> { ["GIT_TERMINAL_PROMPT"] = "0" });
        if (!merge.Success)
            throw new InvalidOperationException($"History stitch merge failed:\n{merge.Output}");

        var mergeHead = await GitCli.RunAsync(RepositoryPath, ["rev-parse", "--verify", "HEAD^{commit}"]);
        if (!mergeHead.Success)
            throw new InvalidOperationException($"History stitch completed, but Gitster could not read the new HEAD:\n{mergeHead.Output}");

        HeadChanged?.Invoke(this, EventArgs.Empty);
        return new HistoryStitchResult(
            sourceRef,
            source.Stdout.Trim(),
            targetBranch,
            backupBranch,
            mergeHead.Stdout.Trim());
    }

    public Task<string> GetReflogSelectorForHeadAsync()         => NS<string>();
    public Task ResetMixedAsync(string targetReference, string? branchName = null) => NSVoid();
    public Task ResetHardAsync(string targetReference, string? branchName = null)  => NSVoid();
    public Task<string> GetHeadShaAsync()                       => NS<string>();
    public Task<string> ResolveRefAsync(string refSpec)         => NS<string>();
    public Task<IReadOnlyList<CommitInfo>> GetCommitsBetweenAsync(string a, string b) => NS<IReadOnlyList<CommitInfo>>();
    public Task<bool> CommitExistsAsync(string sha)             => NS<bool>();
    public Task CheckoutCommitDetachedAsync(string sha)         => NSVoid();
    public Task CherryPickAsync(string sha)                     => NSVoid();
    public Task<string> CreateTagAsync(string name, string targetSha) => NS<string>();
    public async Task<IReadOnlyList<string>> GetTagsForCommitAsync(string sha)
    {
        EnsurePath();
        EnsureCli();

        if (string.IsNullOrWhiteSpace(sha))
            throw new ArgumentException("Commit SHA is required.", nameof(sha));

        var resolved = await GitCli.RunAsync(RepositoryPath, ["rev-parse", "--verify", $"{sha}^{{commit}}"]);
        if (!resolved.Success)
            throw new InvalidOperationException($"Commit not found: {sha}");

        var tags = await GitCli.RunAsync(RepositoryPath, ["tag", "--points-at", resolved.Stdout.Trim()]);
        if (!tags.Success)
            throw new InvalidOperationException($"Could not list tags for commit:\n{tags.Output}");

        return tags.Stdout
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();
    }

    public async Task PushTagAsync(string tagName, string remoteName = "origin", CancellationToken ct = default)
    {
        EnsurePath();
        EnsureCli();

        var name = NormalizeTagName(tagName);
        var remote = NormalizeRemoteName(remoteName);
        await EnsureRemoteExistsAsync(remote, ct);
        var tagRef = $"refs/tags/{name}";

        var verify = await GitCli.RunAsync(RepositoryPath, ["rev-parse", "--verify", $"{tagRef}^{{object}}"], ct: ct);
        if (!verify.Success)
            throw new InvalidOperationException($"Tag not found: {name}");

        var push = await GitCli.RunAsync(
            RepositoryPath,
            ["push", remote, $"{tagRef}:{tagRef}"],
            NoPromptEnvironment(),
            ct,
            timeout: RemoteOperationTimeout);
        if (!push.Success)
            throw new InvalidOperationException($"Push tag failed:\n{push.Output}");
    }

    public Task RevertCommitAsync(string sha)                   => NSVoid();
    public Task<Dictionary<string, string>> GetAllRefsAsync()   => NS<Dictionary<string, string>>();
    public Task<int> GetStashCountAsync()                       => NS<int>();
    public Task<IReadOnlyList<StashInfo>> GetStashesAsync()     => NS<IReadOnlyList<StashInfo>>();
    public Task<CommitDiff> GetStashDiffAsync(int stashIndex, CancellationToken ct = default) => NS<CommitDiff>();
    public Task ApplyStashAsync(int stashIndex, bool reinstateIndex = true) => NSVoid();
    public Task PopStashAsync(int stashIndex, bool reinstateIndex = true)   => NSVoid();
    public Task DropStashAsync(int stashIndex)                  => NSVoid();
    public Task<string> CreateStashAsync(string message, bool includeUntracked = true) => NS<string>();
    public Task<string> ConvertStashToBranchAsync(int stashIndex, string branchName)   => NS<string>();
    public Task<IReadOnlyList<BranchSummary>> GetBranchesAsync()               => NS<IReadOnlyList<BranchSummary>>();
    public Task<IReadOnlyList<CommitInfo>> GetCommitsForRefAsync(string refName, int maxCount = 200) => NS<IReadOnlyList<CommitInfo>>();
    public Task<bool> AreCommitsContiguousAsync(IReadOnlyList<string> shas)    => NS<bool>();
    public Task ReorderCommitsAsync(IReadOnlyList<string> shasNewestFirst, IReadOnlyList<string> reorderedShasNewestFirst, string? branchName = null) => NSVoid();
    public Task SplitCommitAsync(string sha, IReadOnlyList<string> firstCommitPaths, string firstMessage, string secondMessage, string? branchName = null) => NSVoid();
    public Task<string> CreateOrphanBranchAsync(string branchName, bool commitCurrentTree) => NS<string>();
    public Task<string> RescueDetachedHeadAsync(string branchName) => NS<string>();

    // Branch ops / commit-to-branch / snapshot are libgit2 — never routed here.
    public Task<IReadOnlyList<BranchListItem>> GetBranchListAsync()            => NS<IReadOnlyList<BranchListItem>>();
    public Task CheckoutBranchAsync(string branchName)                        => NSVoid();
    public Task<string> CreateBranchAsync(string name, string startPointSha)  => NS<string>();
    public Task DeleteBranchAsync(string name, bool force)                    => NSVoid();
    public Task RenameBranchAsync(string oldName, string newName)             => NSVoid();
    public Task<BranchMergeResult> MergeBranchAsync(string branchName, BranchMergeStrategy strategy) => NS<BranchMergeResult>();
    public Task<HistoryStitchPreview> PreviewHistoryStitchAsync(string sourceRef) => NS<HistoryStitchPreview>();
    public Task<string> CommitToBranchAsync(CommitToBranchRequest request)    => NS<string>();
    public Task<string> CreateSnapshotBranchAsync(string branchName, bool includeUncommitted) => NS<string>();

    // Reword for HEAD is NOT routed here — HybridGitBackend sends it to LibGit2Backend
    public Task RewordCommitHeadAsync(string newMessage) => NSVoid();

    // -- Source archive -----------------------------------------------------

    public async Task<ArchiveResult> ArchiveSourceZipAsync(ArchiveRequest request, CancellationToken ct = default)
    {
        EnsurePath();
        EnsureCli();

        if (string.IsNullOrWhiteSpace(request.TreeishSha))
            throw new ArgumentException("Archive ref cannot be empty.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.OutputPath))
            throw new ArgumentException("Archive output path cannot be empty.", nameof(request));

        var outputPath = Path.GetFullPath(request.OutputPath);
        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new InvalidOperationException("Archive output path must include a directory.");

        Directory.CreateDirectory(outputDirectory);

        var resolvedSha = await ResolveArchiveCommitAsync(request.TreeishSha, ct);
        var prefix = NormalizeArchivePrefix(request.Prefix);
        var tempPath = Path.Combine(outputDirectory, $".gitster-archive-{Guid.NewGuid():N}.tmp");

        try
        {
            var args = new List<string>
            {
                "archive",
                "--format=zip",
                "--output",
                tempPath,
            };

            if (!string.IsNullOrEmpty(prefix))
            {
                args.Add("--prefix");
                args.Add(prefix);
            }

            args.Add(resolvedSha);

            var archive = await GitCli.RunAsync(
                RepositoryPath,
                args,
                new Dictionary<string, string> { ["GIT_TERMINAL_PROMPT"] = "0" },
                ct);

            if (!archive.Success)
                throw new InvalidOperationException($"Archive failed:\n{archive.Output}");

            try
            {
                File.Move(tempPath, outputPath, overwrite: true);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Could not write archive '{outputPath}':\n{ex.Message}",
                    ex);
            }

            var size = new FileInfo(outputPath).Length;
            return new ArchiveResult(outputPath, resolvedSha, size);
        }
        catch
        {
            GitCli.CleanupTemp(tempPath);
            throw;
        }
    }

    private async Task<string> ResolveArchiveCommitAsync(string treeish, CancellationToken ct)
    {
        var result = await GitCli.RunAsync(
            RepositoryPath,
            ["rev-parse", "--verify", $"{treeish}^{{commit}}"],
            new Dictionary<string, string> { ["GIT_TERMINAL_PROMPT"] = "0" },
            ct);

        if (!result.Success)
            throw new InvalidOperationException($"Could not resolve archive ref '{treeish}':\n{result.Output}");

        return result.Stdout.Trim();
    }

    private static string NormalizeArchivePrefix(string prefix)
    {
        var normalized = prefix.Trim().Replace('\\', '/').Trim('/');
        return normalized.Length == 0 ? string.Empty : normalized + "/";
    }

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

    private async Task<string> ResolveCommitAsync(string sha)
    {
        var result = await GitCli.RunAsync(RepositoryPath, ["rev-parse", "--verify", $"{sha}^{{commit}}"]);
        if (!result.Success)
            throw new InvalidOperationException($"Commit not found: {sha}");

        return result.Stdout.Trim();
    }

    private static string BuildFixupTodoEditorScript(string source7, string target7)
    {
        return "@powershell -NoProfile -Command \"& { " +
               "$f = '%1'; " +
               "$lines = Get-Content $f; " +
               $"$src = $lines | Where-Object {{ $_ -match '^pick {source7}\\b' }} | Select-Object -First 1; " +
               "if ($src -eq $null) { exit 2 }; " +
               "$out = New-Object System.Collections.Generic.List[string]; " +
               $"foreach ($line in $lines) {{ if ($line -match '^pick {source7}\\b') {{ continue }} $out.Add($line); if ($line -match '^pick {target7}\\b') {{ $out.Add(($src -replace '^pick', 'fixup')) }} }} " +
               "[IO.File]::WriteAllLines($f, $out) " +
               "}\"";
    }

    private async Task EnsureNotShallowRepositoryAsync(string operation)
    {
        var shallow = await GitCli.RunAsync(RepositoryPath, ["rev-parse", "--is-shallow-repository"]);
        if (shallow.Success && shallow.Stdout.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Cannot {operation} in a shallow clone because the rewrite may need commits outside the local history. " +
                "Fetch the full history, then retry.");
        }
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

    private async Task<string> BuildAvailableBackupBranchNameAsync(string targetBranch, string headSha)
    {
        var stem = $"backup/{SanitizeBranchSegment(targetBranch)}-before-history-stitch-{ShortSha(headSha)}-{DateTime.Now:yyyyMMddHHmmss}";
        for (var i = 0; i < 20; i++)
        {
            var candidate = i == 0 ? stem : $"{stem}-{i + 1}";
            var exists = await GitCli.RunAsync(RepositoryPath, ["rev-parse", "--verify", $"refs/heads/{candidate}"]);
            if (!exists.Success)
                return candidate;
        }

        throw new InvalidOperationException("Could not find an available backup branch name.");
    }

    private static string SanitizeBranchSegment(string value)
    {
        var chars = value
            .Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-')
            .ToArray();
        var result = new string(chars).Trim('-', '.');
        while (result.Contains("--", StringComparison.Ordinal))
            result = result.Replace("--", "-", StringComparison.Ordinal);

        return string.IsNullOrWhiteSpace(result) ? "branch" : result;
    }

    private async Task EnsureRemoteExistsAsync(string remoteName, CancellationToken ct = default)
    {
        var remote = await GitCli.RunAsync(RepositoryPath, ["remote", "get-url", remoteName], ct: ct);
        if (!remote.Success)
            throw new InvalidOperationException($"Remote not found: {remoteName}");
    }

    private static string NormalizeRemoteName(string remoteName) =>
        string.IsNullOrWhiteSpace(remoteName) ? "origin" : remoteName.Trim();

    private static Dictionary<string, string> NoPromptEnvironment() =>
        new() { ["GIT_TERMINAL_PROMPT"] = "0" };

    private static string NormalizeTagName(string tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName))
            throw new ArgumentException("Tag name is required.", nameof(tagName));

        var name = tagName.Trim();
        const string prefix = "refs/tags/";
        return name.StartsWith(prefix, StringComparison.Ordinal)
            ? name[prefix.Length..]
            : name;
    }

    private static string ShortSha(string sha) =>
        sha.Length >= 7 ? sha[..7] : sha;

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
