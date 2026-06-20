using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

using Gitster.Models;
using Gitster.Services.Git;
using Gitster.Services.Search;

using LibGit2Sharp;

using Microsoft.Data.Sqlite;

namespace Gitster.Services.History;

public sealed class CommitHistoryService
{
    public const int DefaultPageSize = 200;

    private readonly IGitBackend _git;
    private readonly string _cacheRoot;
    private readonly string _dbPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private HistoryContext? _context;

    public CommitHistoryService(IGitBackend git)
        : this(git, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Gitster",
            "history-cache"))
    {
    }

    public CommitHistoryService(IGitBackend git, string cacheRoot)
    {
        _git = git;
        _cacheRoot = cacheRoot;
        _dbPath = Path.Combine(_cacheRoot, "history.sqlite");
    }

    public async Task OpenAsync(
        string repoPath,
        CancellationToken ct = default,
        IProgress<RepositoryLoadProgress>? progress = null)
    {
        progress?.Report(new RepositoryLoadProgress(
            "Opening repository",
            repoPath));
        await _git.OpenAsync(repoPath);
        await Task.Run(() =>
        {
            _gate.Wait(ct);
            try
            {
                progress?.Report(new RepositoryLoadProgress(
                    "Validating history cache",
                    "Checking cached branch state."));
                EnsureDatabase();
                using var repo = new Repository(repoPath);
                var context = BuildContext(repo);
                using var conn = OpenConnection();

                var existing = GetBranchState(conn, context);
                UpsertBranch(conn, context, existing?.IsComplete ?? false, existing?.CachedCount ?? 0);

                if (existing != null
                    && existing.CachedCount > 0
                    && !string.Equals(existing.HeadSha, context.HeadSha, StringComparison.OrdinalIgnoreCase))
                {
                    ValidateHeadChange(conn, repo, context, existing, ct, progress);
                }

                _context = context;
            }
            finally
            {
                _gate.Release();
            }
        }, ct);
    }

    public async Task<IReadOnlyList<CommitHistoryRow>> GetPageAsync(
        CommitQuery query,
        int offset,
        int count,
        CancellationToken ct = default,
        IProgress<RepositoryLoadProgress>? progress = null)
    {
        await EnsureContextAsync(ct);

        if (!query.IsEmpty)
        {
            var all = await SearchAsync(query, int.MaxValue, ct);
            return all.Skip(offset).Take(count).ToList();
        }

        await _gate.WaitAsync(ct);
        try
        {
            var context = RequireContext();
            using var repo = new Repository(context.RepoPath);
            using var conn = OpenConnection();
            EnsureCachedCount(conn, repo, context, offset + count, ct, progress);
            return ReadRows(conn, context, offset, count);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<CommitHistoryRow>> SearchAsync(
        CommitQuery query,
        int maxResults,
        CancellationToken ct = default)
    {
        await EnsureCompleteAsync(progress: null, ct);

        await _gate.WaitAsync(ct);
        try
        {
            var context = RequireContext();
            using var conn = OpenConnection();
            var rows = ReadRows(conn, context, 0, int.MaxValue);
            return rows
                .Where(r => query.Matches(r.Message, r.AuthorName, r.AuthorEmail, r.FullSha, r.Date))
                .Take(maxResults)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<CommitHistoryRow>> EnsureCompleteAsync(
        IProgress<RepositoryLoadProgress>? progress,
        CancellationToken ct = default)
    {
        await EnsureContextAsync(ct);

        return await Task.Run(() =>
        {
            _gate.Wait(ct);
            try
            {
                var context = RequireContext();
                using var conn = OpenConnection();
                var state = GetBranchState(conn, context);
                if (state?.IsComplete == true)
                {
                    var cachedRows = ReadRows(conn, context, 0, int.MaxValue);
                    progress?.Report(new RepositoryLoadProgress(
                        "Loading history",
                        "History cache is ready.",
                        cachedRows.Count,
                        TotalCommitCount: cachedRows.Count));
                    return cachedRows;
                }

                using var repo = new Repository(context.RepoPath);
                var rows = WalkRows(repo, int.MaxValue, ct, progress);
                ReplaceRows(conn, context, rows, isComplete: true);
                return rows;
            }
            finally
            {
                _gate.Release();
            }
        }, ct);
    }

    public async Task<IReadOnlyList<AuthorEntry>> GetAuthorsAsync(CancellationToken ct = default)
    {
        await EnsureContextAsync(ct);

        await _gate.WaitAsync(ct);
        try
        {
            var context = RequireContext();
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT author_name, author_email
                FROM history_commits
                WHERE repo_key = $repo AND branch_name = $branch AND author_name <> ''
                GROUP BY lower(author_name), lower(author_email), author_name, author_email
                ORDER BY lower(author_name), lower(author_email);
                """;
            cmd.Parameters.AddWithValue("$repo", context.RepoKey);
            cmd.Parameters.AddWithValue("$branch", context.BranchName);

            var authors = new List<AuthorEntry>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                authors.Add(new AuthorEntry(
                    reader.GetString(0),
                    reader.IsDBNull(1) ? string.Empty : reader.GetString(1)));
            }

            return authors;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<CommitInfo>> GetCommitsByAuthorAsync(
        AuthorEntry author,
        CancellationToken ct = default)
    {
        var rows = await EnsureCompleteAsync(progress: null, ct);
        return rows
            .Where(r =>
                string.Equals(r.AuthorName, author.Name, StringComparison.Ordinal) &&
                string.Equals(r.AuthorEmail, author.Email, StringComparison.OrdinalIgnoreCase))
            .Select(r => r.ToCommitInfo())
            .ToList();
    }

    public async Task ApplyRemoteSetsAsync(RemoteSets sets, CancellationToken ct = default)
    {
        await EnsureContextAsync(ct);

        await _gate.WaitAsync(ct);
        try
        {
            var context = RequireContext();
            using var conn = OpenConnection();
            using var tx = conn.BeginTransaction();

            using (var reset = conn.CreateCommand())
            {
                reset.Transaction = tx;
                reset.CommandText = """
                    UPDATE history_commits
                    SET remote_state = $state, orphaned_pair_sha = NULL
                    WHERE repo_key = $repo AND branch_name = $branch;
                    """;
                reset.Parameters.AddWithValue("$state", (int)(sets.HasTrackingBranch
                    ? CommitRemoteState.OnRemote
                    : CommitRemoteState.NoTrackingBranch));
                reset.Parameters.AddWithValue("$repo", context.RepoKey);
                reset.Parameters.AddWithValue("$branch", context.BranchName);
                reset.ExecuteNonQuery();
            }

            using (var outgoing = conn.CreateCommand())
            {
                outgoing.Transaction = tx;
                outgoing.CommandText = """
                    UPDATE history_commits
                    SET remote_state = $state
                    WHERE repo_key = $repo AND branch_name = $branch AND full_sha = $sha;
                    """;
                outgoing.Parameters.AddWithValue("$state", (int)CommitRemoteState.LocalOnly);
                outgoing.Parameters.AddWithValue("$repo", context.RepoKey);
                outgoing.Parameters.AddWithValue("$branch", context.BranchName);
                var sha = outgoing.Parameters.Add("$sha", SqliteType.Text);
                foreach (var fullSha in sets.OutgoingFullShas)
                {
                    ct.ThrowIfCancellationRequested();
                    sha.Value = fullSha;
                    outgoing.ExecuteNonQuery();
                }
            }

            using (var orphan = conn.CreateCommand())
            {
                orphan.Transaction = tx;
                orphan.CommandText = """
                    UPDATE history_commits
                    SET orphaned_pair_sha = $pair
                    WHERE repo_key = $repo AND branch_name = $branch AND full_sha = $sha;
                    """;
                orphan.Parameters.AddWithValue("$repo", context.RepoKey);
                orphan.Parameters.AddWithValue("$branch", context.BranchName);
                var sha = orphan.Parameters.Add("$sha", SqliteType.Text);
                var pair = orphan.Parameters.Add("$pair", SqliteType.Text);
                foreach (var kv in sets.OrphanedPairs)
                {
                    ct.ThrowIfCancellationRequested();
                    sha.Value = kv.Key;
                    pair.Value = kv.Value;
                    orphan.ExecuteNonQuery();
                }
            }

            tx.Commit();
        }
        finally
        {
            _gate.Release();
        }
    }

    private void EnsureCachedCount(
        SqliteConnection conn,
        Repository repo,
        HistoryContext context,
        int requiredCount,
        CancellationToken ct,
        IProgress<RepositoryLoadProgress>? progress)
    {
        var state = GetBranchState(conn, context);
        if (state?.IsComplete == true || (state?.CachedCount ?? 0) >= requiredCount)
            return;

        var rows = WalkRows(repo, requiredCount, ct, progress);
        var isComplete = rows.Count < requiredCount;
        ReplaceRows(conn, context, rows, isComplete);
    }

    private void ValidateHeadChange(
        SqliteConnection conn,
        Repository repo,
        HistoryContext context,
        BranchState existing,
        CancellationToken ct,
        IProgress<RepositoryLoadProgress>? progress)
    {
        var cachedSequences = ReadCachedSequences(conn, context);
        if (cachedSequences.Count == 0)
        {
            UpsertBranch(conn, context, isComplete: false, cachedCount: 0);
            return;
        }

        var prefix = new List<CommitHistoryRow>();
        int? foundSequence = null;
        int scanned = 0;
        foreach (var commit in QueryHead(repo))
        {
            ct.ThrowIfCancellationRequested();
            scanned++;
            if (scanned % 500 == 0)
            {
                progress?.Report(new RepositoryLoadProgress(
                    "Validating history cache",
                    "Scanning new history until a cached commit is found.",
                    scanned));
            }

            if (cachedSequences.TryGetValue(commit.Id.Sha, out var sequence))
            {
                foundSequence = sequence;
                break;
            }

            prefix.Add(ToRow(commit, prefix.Count));
        }

        if (foundSequence.HasValue)
        {
            RebaseRowsToKnownSha(conn, context, prefix, foundSequence.Value, existing.IsComplete);
            return;
        }

        ReplaceRows(conn, context, prefix, isComplete: true);
    }

    private void RebaseRowsToKnownSha(
        SqliteConnection conn,
        HistoryContext context,
        IReadOnlyList<CommitHistoryRow> prefix,
        int knownSequence,
        bool oldComplete)
    {
        using var tx = conn.BeginTransaction();

        using (var delete = conn.CreateCommand())
        {
            delete.Transaction = tx;
            delete.CommandText = """
                DELETE FROM history_commits
                WHERE repo_key = $repo AND branch_name = $branch AND sequence < $known;
                """;
            delete.Parameters.AddWithValue("$repo", context.RepoKey);
            delete.Parameters.AddWithValue("$branch", context.BranchName);
            delete.Parameters.AddWithValue("$known", knownSequence);
            delete.ExecuteNonQuery();
        }

        using (var moveAway = conn.CreateCommand())
        {
            moveAway.Transaction = tx;
            moveAway.CommandText = """
                UPDATE history_commits
                SET sequence = sequence + 1000000000
                WHERE repo_key = $repo AND branch_name = $branch;
                """;
            moveAway.Parameters.AddWithValue("$repo", context.RepoKey);
            moveAway.Parameters.AddWithValue("$branch", context.BranchName);
            moveAway.ExecuteNonQuery();
        }

        using (var shift = conn.CreateCommand())
        {
            shift.Transaction = tx;
            shift.CommandText = """
                UPDATE history_commits
                SET sequence = sequence - 1000000000 - $known + $prefix
                WHERE repo_key = $repo AND branch_name = $branch;
                """;
            shift.Parameters.AddWithValue("$repo", context.RepoKey);
            shift.Parameters.AddWithValue("$branch", context.BranchName);
            shift.Parameters.AddWithValue("$known", knownSequence);
            shift.Parameters.AddWithValue("$prefix", prefix.Count);
            shift.ExecuteNonQuery();
        }

        InsertRows(conn, tx, context, prefix);
        tx.Commit();

        UpsertBranch(conn, context, oldComplete, CountRows(conn, context));
    }

    private void ReplaceRows(
        SqliteConnection conn,
        HistoryContext context,
        IReadOnlyList<CommitHistoryRow> rows,
        bool isComplete)
    {
        using var tx = conn.BeginTransaction();
        using (var delete = conn.CreateCommand())
        {
            delete.Transaction = tx;
            delete.CommandText = """
                DELETE FROM history_commits
                WHERE repo_key = $repo AND branch_name = $branch;
                """;
            delete.Parameters.AddWithValue("$repo", context.RepoKey);
            delete.Parameters.AddWithValue("$branch", context.BranchName);
            delete.ExecuteNonQuery();
        }

        InsertRows(conn, tx, context, rows);
        tx.Commit();

        UpsertBranch(conn, context, isComplete, rows.Count);
    }

    private static void InsertRows(
        SqliteConnection conn,
        SqliteTransaction tx,
        HistoryContext context,
        IReadOnlyList<CommitHistoryRow> rows)
    {
        using var insert = conn.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = """
            INSERT OR REPLACE INTO history_commits
                (repo_key, branch_name, sequence, full_sha, short_sha, message, author_name,
                 author_email, author_date_ticks, tree_sha, remote_state, orphaned_pair_sha)
            VALUES
                ($repo, $branch, $sequence, $full, $short, $message, $author, $email,
                 $date, $tree, $remote, $orphan);
            """;
        insert.Parameters.AddWithValue("$repo", context.RepoKey);
        insert.Parameters.AddWithValue("$branch", context.BranchName);
        var sequence = insert.Parameters.Add("$sequence", SqliteType.Integer);
        var full = insert.Parameters.Add("$full", SqliteType.Text);
        var shortSha = insert.Parameters.Add("$short", SqliteType.Text);
        var message = insert.Parameters.Add("$message", SqliteType.Text);
        var author = insert.Parameters.Add("$author", SqliteType.Text);
        var email = insert.Parameters.Add("$email", SqliteType.Text);
        var date = insert.Parameters.Add("$date", SqliteType.Integer);
        var tree = insert.Parameters.Add("$tree", SqliteType.Text);
        var remote = insert.Parameters.Add("$remote", SqliteType.Integer);
        var orphan = insert.Parameters.Add("$orphan", SqliteType.Text);

        foreach (var row in rows)
        {
            sequence.Value = row.Sequence;
            full.Value = row.FullSha;
            shortSha.Value = row.ShortSha;
            message.Value = row.Message;
            author.Value = row.AuthorName;
            email.Value = row.AuthorEmail;
            date.Value = row.Date.Ticks;
            tree.Value = row.TreeSha;
            remote.Value = (int)row.RemoteState;
            orphan.Value = (object?)row.OrphanedPairSha ?? DBNull.Value;
            insert.ExecuteNonQuery();
        }
    }

    private List<CommitHistoryRow> WalkRows(
        Repository repo,
        int maxCount,
        CancellationToken ct,
        IProgress<RepositoryLoadProgress>? progress)
    {
        var totalCount = maxCount == int.MaxValue
            ? CountHeadCommits(repo, ct, progress)
            : maxCount;
        var rows = new List<CommitHistoryRow>(maxCount == int.MaxValue ? Math.Max(totalCount, 1) : maxCount);

        progress?.Report(new RepositoryLoadProgress(
            "Loading history",
            "Walking commits from HEAD.",
            0,
            TotalCommitCount: totalCount));

        foreach (var commit in QueryHead(repo))
        {
            ct.ThrowIfCancellationRequested();
            rows.Add(ToRow(commit, rows.Count));
            if (rows.Count % 100 == 0)
            {
                progress?.Report(new RepositoryLoadProgress(
                    "Loading history",
                    "Walking commits from HEAD.",
                    rows.Count,
                    TotalCommitCount: totalCount));
            }
            if (rows.Count >= maxCount)
                break;
        }

        progress?.Report(new RepositoryLoadProgress(
            "Loading history",
            "History cache is ready.",
            rows.Count,
            TotalCommitCount: Math.Max(totalCount, rows.Count)));
        return rows;
    }

    private static int CountHeadCommits(
        Repository repo,
        CancellationToken ct,
        IProgress<RepositoryLoadProgress>? progress)
    {
        progress?.Report(new RepositoryLoadProgress(
            "Counting history",
            "Counting commits for determinate progress."));

        var count = 0;
        foreach (var _ in QueryHead(repo))
        {
            ct.ThrowIfCancellationRequested();
            count++;
            if (count % 1000 == 0)
            {
                progress?.Report(new RepositoryLoadProgress(
                    "Counting history",
                    "Counting commits for determinate progress.",
                    count));
            }
        }

        return count;
    }

    private static IEnumerable<Commit> QueryHead(Repository repo)
    {
        if (repo.Head?.Tip == null)
            return [];

        return repo.Commits.QueryBy(new LibGit2Sharp.CommitFilter
        {
            SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time,
            IncludeReachableFrom = repo.Head.Tip,
        });
    }

    private static CommitHistoryRow ToRow(Commit commit, int sequence) => new(
        sequence,
        commit.Id.Sha,
        commit.Id.Sha.Length >= 7 ? commit.Id.Sha[..7] : commit.Id.Sha,
        commit.MessageShort,
        commit.Author.When.DateTime,
        commit.Author.Name ?? string.Empty,
        commit.Author.Email ?? string.Empty,
        commit.Tree?.Sha ?? string.Empty,
        CommitRemoteState.OnRemote,
        null);

    private IReadOnlyList<CommitHistoryRow> ReadRows(
        SqliteConnection conn,
        HistoryContext context,
        int offset,
        int count)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT sequence, full_sha, short_sha, message, author_date_ticks, author_name,
                   author_email, tree_sha, remote_state, orphaned_pair_sha
            FROM history_commits
            WHERE repo_key = $repo AND branch_name = $branch
            ORDER BY sequence
            LIMIT $count OFFSET $offset;
            """;
        cmd.Parameters.AddWithValue("$repo", context.RepoKey);
        cmd.Parameters.AddWithValue("$branch", context.BranchName);
        cmd.Parameters.AddWithValue("$count", count == int.MaxValue ? -1 : count);
        cmd.Parameters.AddWithValue("$offset", offset);

        var rows = new List<CommitHistoryRow>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            rows.Add(ReadRow(reader));
        return rows;
    }

    private static CommitHistoryRow ReadRow(SqliteDataReader reader) => new(
        reader.GetInt32(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        new DateTime(reader.GetInt64(4)),
        reader.GetString(5),
        reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
        reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
        (CommitRemoteState)reader.GetInt32(8),
        reader.IsDBNull(9) ? null : reader.GetString(9));

    private Dictionary<string, int> ReadCachedSequences(SqliteConnection conn, HistoryContext context)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT full_sha, sequence
            FROM history_commits
            WHERE repo_key = $repo AND branch_name = $branch;
            """;
        cmd.Parameters.AddWithValue("$repo", context.RepoKey);
        cmd.Parameters.AddWithValue("$branch", context.BranchName);

        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result[reader.GetString(0)] = reader.GetInt32(1);
        return result;
    }

    private int CountRows(SqliteConnection conn, HistoryContext context)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*)
            FROM history_commits
            WHERE repo_key = $repo AND branch_name = $branch;
            """;
        cmd.Parameters.AddWithValue("$repo", context.RepoKey);
        cmd.Parameters.AddWithValue("$branch", context.BranchName);
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private BranchState? GetBranchState(SqliteConnection conn, HistoryContext context)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT head_sha, upstream_sha, is_complete, cached_count
            FROM history_branches
            WHERE repo_key = $repo AND branch_name = $branch;
            """;
        cmd.Parameters.AddWithValue("$repo", context.RepoKey);
        cmd.Parameters.AddWithValue("$branch", context.BranchName);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;

        return new BranchState(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.GetInt32(2) != 0,
            reader.GetInt32(3));
    }

    private void UpsertBranch(
        SqliteConnection conn,
        HistoryContext context,
        bool isComplete,
        int cachedCount)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO history_branches
                (repo_key, branch_name, repo_path, git_dir, head_sha, upstream_sha,
                 is_complete, cached_count, updated_at)
            VALUES
                ($repo, $branch, $path, $git, $head, $upstream, $complete, $count, $updated)
            ON CONFLICT(repo_key, branch_name) DO UPDATE SET
                repo_path = excluded.repo_path,
                git_dir = excluded.git_dir,
                head_sha = excluded.head_sha,
                upstream_sha = excluded.upstream_sha,
                is_complete = excluded.is_complete,
                cached_count = excluded.cached_count,
                updated_at = excluded.updated_at;
            """;
        cmd.Parameters.AddWithValue("$repo", context.RepoKey);
        cmd.Parameters.AddWithValue("$branch", context.BranchName);
        cmd.Parameters.AddWithValue("$path", context.RepoPath);
        cmd.Parameters.AddWithValue("$git", context.GitDir);
        cmd.Parameters.AddWithValue("$head", context.HeadSha);
        cmd.Parameters.AddWithValue("$upstream", (object?)context.UpstreamSha ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$complete", isComplete ? 1 : 0);
        cmd.Parameters.AddWithValue("$count", cachedCount);
        cmd.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
    }

    private async Task EnsureContextAsync(CancellationToken ct)
    {
        if (_context != null)
            return;

        if (string.IsNullOrWhiteSpace(_git.RepositoryPath))
            throw new InvalidOperationException("No repository is open.");

        await OpenAsync(_git.RepositoryPath, ct);
    }

    private HistoryContext RequireContext() =>
        _context ?? throw new InvalidOperationException("No repository is open.");

    private static HistoryContext BuildContext(Repository repo)
    {
        var workDir = repo.Info.WorkingDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var gitDir = repo.Info.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var headSha = repo.Head.Tip?.Sha ?? string.Empty;
        var branchName = repo.Info.IsHeadDetached
            ? $"detached:{Short(headSha)}"
            : repo.Head.FriendlyName;
        var upstreamSha = repo.Head.TrackedBranch?.Tip?.Sha;
        var repoKey = Sha256($"{gitDir.ToLowerInvariant()}|{workDir.ToLowerInvariant()}");
        return new HistoryContext(repoKey, workDir, gitDir, branchName, headSha, upstreamSha);
    }

    private static string Short(string sha) =>
        sha.Length >= 7 ? sha[..7] : sha;

    private void EnsureDatabase()
    {
        Directory.CreateDirectory(_cacheRoot);
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode = WAL;

            CREATE TABLE IF NOT EXISTS history_branches (
                repo_key TEXT NOT NULL,
                branch_name TEXT NOT NULL,
                repo_path TEXT NOT NULL,
                git_dir TEXT NOT NULL,
                head_sha TEXT NOT NULL,
                upstream_sha TEXT NULL,
                is_complete INTEGER NOT NULL,
                cached_count INTEGER NOT NULL,
                updated_at TEXT NOT NULL,
                PRIMARY KEY(repo_key, branch_name)
            );

            CREATE TABLE IF NOT EXISTS history_commits (
                repo_key TEXT NOT NULL,
                branch_name TEXT NOT NULL,
                sequence INTEGER NOT NULL,
                full_sha TEXT NOT NULL,
                short_sha TEXT NOT NULL,
                message TEXT NOT NULL,
                author_name TEXT NOT NULL,
                author_email TEXT NOT NULL,
                author_date_ticks INTEGER NOT NULL,
                tree_sha TEXT NOT NULL,
                remote_state INTEGER NOT NULL,
                orphaned_pair_sha TEXT NULL,
                PRIMARY KEY(repo_key, branch_name, full_sha)
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ix_history_commits_sequence
                ON history_commits(repo_key, branch_name, sequence);

            CREATE INDEX IF NOT EXISTS ix_history_commits_author
                ON history_commits(repo_key, branch_name, author_name, author_email);
            """;
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        };
        var conn = new SqliteConnection(builder.ToString());
        conn.Open();
        return conn;
    }

    private static string Sha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
            sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    private sealed record HistoryContext(
        string RepoKey,
        string RepoPath,
        string GitDir,
        string BranchName,
        string HeadSha,
        string? UpstreamSha);

    private sealed record BranchState(
        string HeadSha,
        string? UpstreamSha,
        bool IsComplete,
        int CachedCount);
}
