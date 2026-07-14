namespace Gitster.Core.Models;

public enum DiffLineKind { Context, Added, Removed, Hunk }

/// <summary>A single line of a unified diff (B7 inline rendering).</summary>
public sealed record DiffLine(DiffLineKind Kind, string Text);

/// <summary>
/// A changed file in a diff: status badge + line counts, plus optional unified-diff
/// <see cref="Lines"/> for inline rendering and per-file collapse (B7).
/// </summary>
public sealed record DiffFileEntry(
    string Path,
    int Added,
    int Deleted,
    string Status = "M",
    IReadOnlyList<DiffLine>? Lines = null);
