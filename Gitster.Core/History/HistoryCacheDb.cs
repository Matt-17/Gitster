using System.IO;
using Microsoft.Data.Sqlite;

namespace Gitster.Core.History;

public sealed class HistoryCacheDb
{
    public HistoryCacheDb(string cacheRoot)
    {
        CacheRoot = cacheRoot;
        DbPath = Path.Combine(CacheRoot, "history.sqlite");
    }

    public string CacheRoot { get; }
    public string DbPath { get; }

    public void EnsureDirectory() => Directory.CreateDirectory(CacheRoot);

    public void RunWithCorruptionReset(Action action)
    {
        try
        {
            action();
        }
        catch (SqliteException ex) when (IsCorruption(ex))
        {
            Reset();
            action();
        }
    }

    public SqliteConnection OpenConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = DbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
        };
        var conn = new SqliteConnection(builder.ToString());
        conn.Open();
        using var busy = conn.CreateCommand();
        busy.CommandText = "PRAGMA busy_timeout = 5000;";
        busy.ExecuteNonQuery();
        return conn;
    }

    public void Reset()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { DbPath, $"{DbPath}-wal", $"{DbPath}-shm" })
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Another instance may have the cache open. The caller will retry normally later.
            }
        }
        SqliteConnection.ClearAllPools();
    }

    public static bool IsCorruption(SqliteException ex) =>
        ex.SqliteErrorCode is 11 or 26
        || ex.Message.Contains("malformed", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("not a database", StringComparison.OrdinalIgnoreCase);
}
