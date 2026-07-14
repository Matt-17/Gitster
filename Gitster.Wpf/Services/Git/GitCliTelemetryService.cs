using Microsoft.Extensions.Logging;

namespace Gitster.Services.Git;

public sealed class GitCliTelemetryService : IDisposable
{
    private readonly ILogger<GitCliTelemetryService> _logger;

    public GitCliTelemetryService(ILogger<GitCliTelemetryService> logger)
    {
        _logger = logger;
    }

    public void Start() => GitCli.Completed += OnGitCliCompleted;

    public void Dispose() => GitCli.Completed -= OnGitCliCompleted;

    private void OnGitCliCompleted(object? sender, GitCliLogEventArgs e)
    {
        if (e.Canceled)
        {
            _logger.LogInformation(
                "git {Verb} canceled after {DurationMs} ms in {WorkDir}",
                e.Verb,
                (int)e.Duration.TotalMilliseconds,
                e.WorkDir ?? ".");
            return;
        }

        if (e.TimedOut)
        {
            _logger.LogWarning(
                "git {Verb} timed out after {DurationMs} ms in {WorkDir}",
                e.Verb,
                (int)e.Duration.TotalMilliseconds,
                e.WorkDir ?? ".");
            return;
        }

        _logger.LogInformation(
            "git {Verb} completed with exit code {ExitCode} after {DurationMs} ms in {WorkDir}",
            e.Verb,
            e.ExitCode,
            (int)e.Duration.TotalMilliseconds,
            e.WorkDir ?? ".");
    }
}
