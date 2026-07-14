using Gitster.Core.Models;

namespace Gitster.Core.Git;

/// <summary>The file-level diff of a commit against its first parent (empty tree for the root commit).</summary>
public sealed record CommitDiff(IReadOnlyList<DiffFileEntry> Files, int LinesAdded, int LinesDeleted)
{
    public static readonly CommitDiff Empty = new(Array.Empty<DiffFileEntry>(), 0, 0);

    public string Header => Files.Count == 0
        ? string.Empty
        : $"{Files.Count} {(Files.Count == 1 ? "file" : "files")} · +{LinesAdded} −{LinesDeleted}";
}
