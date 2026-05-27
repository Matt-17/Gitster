namespace Gitster.Services.Git;

/// <summary>Describes a single commit to be rewritten during an author-repair sweep.</summary>
public sealed record CommitRewrite(
    string  Sha,
    string? NewAuthorName      = null,
    string? NewAuthorEmail     = null,
    string? NewCommitterName   = null,
    string? NewCommitterEmail  = null,
    DateTimeOffset? NewAuthorDate    = null,
    DateTimeOffset? NewCommitterDate = null,
    string? NewMessage         = null);
