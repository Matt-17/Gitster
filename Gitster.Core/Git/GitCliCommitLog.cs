using System.Globalization;

namespace Gitster.Core.Git;

/// <summary>
/// Reads the checked-out branch's history with the Git CLI: the local commits with their
/// remote state, the incoming commits of the upstream, and the pairing of a rewritten local
/// commit with its remote copy (same tree). Same result as the libgit2 history reader, but
/// every commit is addressed by its full SHA, so an ambiguous short prefix can never break it.
/// </summary>
internal static class GitCliCommitLog
{
    private const char FieldSeparator = '\x1f';

    // Full SHA, tree, parents, author name, author email, author date, committer date, subject.
    private const string Format = "--format=%H%x1f%T%x1f%P%x1f%an%x1f%ae%x1f%aI%x1f%cI%x1f%s";

    public static async Task<IReadOnlyList<CommitInfo>> ReadAsync(
        string repositoryPath,
        CommitFilter? filter,
        CancellationToken ct = default)
    {
        var head = await RevParseAsync(repositoryPath, "HEAD", ct);
        if (head is null)
            return [];

        // The upstream tip, or null without an upstream or when it no longer exists.
        var upstream = await RevParseAsync(repositoryPath, "@{upstream}", ct);

        var local = await LogAsync(repositoryPath, [head], ct);
        if (upstream is null)
        {
            return Filter(local, filter)
                .Select(c => c.ToCommitInfo(CommitRemoteState.NoTrackingBranch))
                .ToList();
        }

        var outgoing = await LogAsync(repositoryPath, [head, "^" + upstream], ct);
        var outgoingShas = outgoing.Select(c => c.Sha).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var incoming = Filter(await LogAsync(repositoryPath, [upstream, "^" + head], ct), filter).ToList();

        // A rewritten commit that was already pushed shows up twice: outgoing locally and
        // incoming from the remote, with the same tree. Each incoming commit pairs with the
        // outgoing commit that has its tree; several incoming commits may share one tree
        // (an empty commit, a merge without file changes), and the outgoing one pairs with
        // the first, newest of them.
        var outgoingByTree = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var commit in outgoing)
            outgoingByTree.TryAdd(commit.Tree, commit.Sha);

        var pairOfIncoming = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pairOfOutgoing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (outgoing.Count > 0)
        {
            foreach (var commit in incoming)
            {
                if (!outgoingByTree.TryGetValue(commit.Tree, out var outgoingSha))
                    continue;
                pairOfIncoming[commit.Sha] = outgoingSha;
                pairOfOutgoing.TryAdd(outgoingSha, commit.Sha);
            }
        }

        var result = new List<CommitInfo>(incoming.Count + local.Count);
        result.AddRange(incoming.Select(c => c.ToCommitInfo(
            CommitRemoteState.Incoming,
            pairOfIncoming.GetValueOrDefault(c.Sha))));
        result.AddRange(Filter(local, filter).Select(c => c.ToCommitInfo(
            outgoingShas.Contains(c.Sha) ? CommitRemoteState.LocalOnly : CommitRemoteState.OnRemote,
            pairOfOutgoing.GetValueOrDefault(c.Sha))));
        return result;
    }

    /// <summary>Author and date filter with the same semantics as the libgit2 reader.</summary>
    private static IEnumerable<LogEntry> Filter(IEnumerable<LogEntry> commits, CommitFilter? filter)
    {
        if (filter is null)
            return commits;

        if (!string.IsNullOrEmpty(filter.SelectedAuthorName) && filter.SelectedAuthorName != "All")
        {
            var author = filter.SelectedAuthorName;
            commits = commits.Where(c => string.Equals(c.AuthorName, author, StringComparison.Ordinal));
        }

        if (filter.FromDate.HasValue)
        {
            var from = filter.FromDate.Value.Date;
            commits = commits.Where(c => c.AuthorDate.Date >= from);
        }

        if (filter.ToDate.HasValue)
        {
            var toExclusive = filter.ToDate.Value.Date.AddDays(1);
            commits = commits.Where(c => c.AuthorDate.DateTime < toExclusive);
        }

        return commits;
    }

    private static async Task<string?> RevParseAsync(string repositoryPath, string revision, CancellationToken ct)
    {
        var result = await GitCli.RunAsync(
            repositoryPath,
            ["rev-parse", "--verify", "--quiet", revision + "^{commit}"],
            ct: ct);
        var sha = result.Stdout.Trim();
        return result.Success && sha.Length > 0 ? sha : null;
    }

    private static async Task<List<LogEntry>> LogAsync(
        string repositoryPath,
        IReadOnlyList<string> revisions,
        CancellationToken ct)
    {
        var args = new List<string>
        {
            "-c", "log.showSignature=false",
            "log", "--date-order", "--no-color", "--no-decorate", "-z", Format,
        };
        args.AddRange(revisions);

        var result = await GitCli.RunAsync(repositoryPath, args, ct: ct, timeout: TimeSpan.FromMinutes(5));
        if (!result.Success)
            throw new InvalidOperationException($"Reading the commit history failed:\n{result.Output}");

        return Parse(result.Stdout);
    }

    internal static List<LogEntry> Parse(string stdout)
    {
        var entries = new List<LogEntry>();
        foreach (var record in stdout.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = record.TrimStart('\n', '\r').Split(FieldSeparator);
            if (fields.Length < 8 || fields[0].Length == 0)
                continue;

            entries.Add(new LogEntry(
                Sha: fields[0],
                Tree: fields[1],
                Parents: fields[2].Split(' ', StringSplitOptions.RemoveEmptyEntries),
                AuthorName: fields[3],
                AuthorEmail: fields[4],
                AuthorDate: ParseDate(fields[5]),
                CommitterDate: ParseDate(fields[6]),
                // A subject can itself contain the separator only in theory; keep it whole.
                Subject: string.Join(FieldSeparator, fields.Skip(7))));
        }

        return entries;
    }

    private static DateTimeOffset ParseDate(string value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : DateTimeOffset.MinValue;

    internal sealed record LogEntry(
        string Sha,
        string Tree,
        IReadOnlyList<string> Parents,
        string AuthorName,
        string AuthorEmail,
        DateTimeOffset AuthorDate,
        DateTimeOffset CommitterDate,
        string Subject)
    {
        // Dates keep the commit's own clock time, as the libgit2 reader does (When.DateTime).
        public CommitInfo ToCommitInfo(CommitRemoteState state, string? pairSha = null) => new(
            GitSha.Short(Sha),
            Subject,
            AuthorDate.DateTime,
            AuthorName,
            AuthorEmail,
            state,
            Sha,
            OrphanedPairSha: pairSha is null ? null : GitSha.Short(pairSha),
            ParentShas: Parents,
            CommitterDate: CommitterDate.DateTime);
    }
}
