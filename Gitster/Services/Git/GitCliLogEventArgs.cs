namespace Gitster.Services.Git;

public sealed record GitCliLogEventArgs(
    string Verb,
    string? WorkDir,
    TimeSpan Duration,
    int? ExitCode,
    bool TimedOut,
    bool Canceled);
