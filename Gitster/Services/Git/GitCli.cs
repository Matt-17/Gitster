using System.Diagnostics;
using System.IO;

namespace Gitster.Services.Git;

/// <summary>
/// Low-level async Git process runner.  All output is captured; no shell window appears.
/// </summary>
public static class GitCli
{
    public static bool IsAvailable { get; private set; }
    public static string? Version { get; private set; }

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
    /// stdout and stderr.  Times out after 60 s.
    /// </summary>
    public static async Task<GitResult> RunAsync(
        string? workDir,
        string args,
        Dictionary<string, string>? env = null,
        CancellationToken ct = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(TimeSpan.FromSeconds(60));
        var token = linkedCts.Token;

        var psi = new ProcessStartInfo("git", args)
        {
            WorkingDirectory        = workDir ?? Environment.CurrentDirectory,
            RedirectStandardOutput  = true,
            RedirectStandardError   = true,
            UseShellExecute         = false,
            CreateNoWindow          = true,
        };

        if (env != null)
            foreach (var (k, v) in env)
                psi.Environment[k] = v;

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = false };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(token);
        var stderrTask = process.StandardError.ReadToEndAsync(token);

        await process.WaitForExitAsync(token);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        return new GitResult(process.ExitCode, stdout.TrimEnd(), stderr.TrimEnd());
    }

    /// <summary>
    /// Writes a batch (.cmd) file to a temp path and returns that path.
    /// Cleans up old gitster temp files on the way.
    /// </summary>
    public static string WriteTempBat(string content, string label = "script")
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"gitster-{label}-{Guid.NewGuid():N}.cmd");
        File.WriteAllText(tmp, content);
        return tmp;
    }

    /// <summary>
    /// Writes a text message file to a temp path and returns that path.
    /// </summary>
    public static string WriteTempMsg(string content)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"gitster-msg-{Guid.NewGuid():N}.txt");
        File.WriteAllText(tmp, content);
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
