namespace Gitster.Core.Models;

public enum CustomToolScope { Global, Repository }

/// <summary>
/// A user-defined command exposed in the Tools menu. Merged from Git's native
/// <c>[guitool]</c> sections and Gitster's own JSON stores (repo overrides global).
/// </summary>
public sealed record CustomTool(
    string Name,              // menu label
    string Command,           // shell command, may contain placeholders
    string? Confirm,          // optional confirmation prompt text (null = no confirm)
    bool NeedsCommit,         // requires a selected commit (passes its sha)
    string? Prompt,           // optional: ask user for a value, substituted as $ARGS
    CustomToolScope Scope)    // Global or Repository
{
    /// <summary>True for tools imported read-only from Git's [guitool] gitconfig sections.</summary>
    public bool FromGitConfig { get; init; }
}
