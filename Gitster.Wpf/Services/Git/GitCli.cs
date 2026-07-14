using System.Diagnostics;
using System.IO;
using System.Text;

namespace Gitster.Services.Git;

/// <summary>
/// Low-level async Git process runner.  All output is captured; no shell window appears.
/// </summary>
public static class GitCli
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    public static bool IsAvailable { get; private set; }
    public static string? Version { get; private set; }
    public static event EventHandler<GitCliLogEventArgs>? Completed;

    static GitCli() => _ = DetectAsync();

    public static async Task DetectAsync()
    {
        try
        {
            var r = await RunAsync(null, "--version");
            IsAvailable = r.Success;
            Version = r.Success ? r.Stdout.Trim() : null;
        }
        catch { IsAvailable = false; }
    }

    /// <summary>
    /// Runs <c>git <paramref name="args"/></c> in <paramref name="workDir"/>, capturing
    /// stdout and stderr.  Times out after 60 s; a hung process is killed (whole tree)
    /// so it can never freeze the app.
    /// </summary>
    public static async Task<GitResult> RunAsync(
        string? workDir,
        string args,
        Dictionary<string, string>? env = null,
        CancellationToken ct = default,
        TimeSpan? timeout = null)
    {
        var psi = new ProcessStartInfo("git", args)
        {
            WorkingDirectory        = workDir ?? Environment.CurrentDirectory,
            RedirectStandardOutput  = true,
            RedirectStandardError   = true,
            StandardOutputEncoding  = Encoding.UTF8,
            StandardErrorEncoding   = Encoding.UTF8,
            UseShellExecute         = false,
            CreateNoWindow          = true,
        };

        var verb = args.Split(' ', 2)[0];
        return await RunProcessAsync(psi, verb, env, ct, timeout);
    }

    /// <summary>
    /// Runs <c>git</c> with exact argument boundaries. Use this for file paths and refs
    /// where string quoting would be fragile.
    /// </summary>
    public static async Task<GitResult> RunAsync(
        string? workDir,
        IReadOnlyList<string> args,
        Dictionary<string, string>? env = null,
        CancellationToken ct = default,
        TimeSpan? timeout = null)
    {
        if (args.Count == 0)
            throw new ArgumentException("At least one Git argument is required.", nameof(args));

        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory        = workDir ?? Environment.CurrentDirectory,
            RedirectStandardOutput  = true,
            RedirectStandardError   = true,
            StandardOutputEncoding  = Encoding.UTF8,
            StandardErrorEncoding   = Encoding.UTF8,
            UseShellExecute         = false,
            CreateNoWindow          = true,
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        return await RunProcessAsync(psi, args[0], env, ct, timeout);
    }

    private static async Task<GitResult> RunProcessAsync(
        ProcessStartInfo psi,
        string verb,
        Dictionary<string, string>? env,
        CancellationToken ct,
        TimeSpan? timeout)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var effectiveTimeout = timeout ?? DefaultTimeout;
        linkedCts.CancelAfter(effectiveTimeout);
        var token = linkedCts.Token;

        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        if (env != null)
            foreach (var (k, v) in env)
                psi.Environment[k] = v;

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = false };
        var started = Stopwatch.StartNew();
        process.Start();

        // Read both streams concurrently so neither buffer can fill and deadlock the
        // other (the classic stdout/stderr pipe-buffer deadlock). The reads are NOT
        // tied to the cancellation token: when we kill the process the pipes close and
        // the reads complete naturally, which keeps them from becoming unobserved tasks.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(token);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
            try { await Task.WhenAll(stdoutTask, stderrTask); } catch { /* drain */ }
            started.Stop();

            if (ct.IsCancellationRequested)
            {
                Completed?.Invoke(null, new GitCliLogEventArgs(
                    verb,
                    psi.WorkingDirectory,
                    started.Elapsed,
                    null,
                    TimedOut: false,
                    Canceled: true));
                throw new OperationCanceledException(ct);
            }

            Completed?.Invoke(null, new GitCliLogEventArgs(
                verb,
                psi.WorkingDirectory,
                started.Elapsed,
                null,
                TimedOut: true,
                Canceled: false));

            throw new TimeoutException(
                $"git {verb} did not finish within {(int)effectiveTimeout.TotalSeconds} seconds and was terminated.");
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        started.Stop();

        Completed?.Invoke(null, new GitCliLogEventArgs(
            verb,
            psi.WorkingDirectory,
            started.Elapsed,
            process.ExitCode,
            TimedOut: false,
            Canceled: false));

        return new GitResult(process.ExitCode, stdout.TrimEnd(), stderr.TrimEnd());
    }

    /// <summary>
    /// Converts a Windows path to a form that survives Git's editor invocation.
    /// Git evaluates <c>GIT_EDITOR</c>/<c>GIT_SEQUENCE_EDITOR</c> through its bundled
    /// <c>sh</c>, which eats backslashes and splits on spaces.  Returning a
    /// single-quoted forward-slash path makes both safe.
    /// </summary>
    public static string ToEditorArg(string path) => "'" + path.Replace('\\', '/') + "'";

    /// <summary>
    /// Writes a batch (.cmd) file to a temp path and returns that path.
    /// Cleans up old gitster temp files on the way.
    /// </summary>
    public static string WriteTempBat(string content, string label = "script")
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"gitster-{label}-{Guid.NewGuid():N}.cmd");
        // cmd.exe parses .cmd files in the console's OEM code page; switch that
        // console to UTF-8 first so non-ASCII content (e.g. temp paths under a
        // non-ASCII user profile) survives. BOM-less, or cmd chokes on line 1.
        File.WriteAllText(
            tmp,
            "@chcp 65001 >nul\r\n" + content,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return tmp;
    }

    /// <summary>
    /// Writes a text message file to a temp path and returns that path.
    /// </summary>
    public static string WriteTempMsg(string content)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"gitster-msg-{Guid.NewGuid():N}.txt");
        // Git reads commit-message files as UTF-8; a BOM would leak into the subject line.
        File.WriteAllText(tmp, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return tmp;
    }

    /// <summary>Deletes temp files, swallowing any error.</summary>
    public static void CleanupTemp(params string?[] paths)
    {
        foreach (var p in paths)
        {
            if (p != null) try { File.Delete(p); } catch { /* best-effort */ }
        }
    }
}

public record GitResult(int ExitCode, string Stdout, string Stderr)
{
    public bool Success => ExitCode == 0;
    public string Output => string.IsNullOrEmpty(Stdout) ? Stderr : Stdout;
}
