using Gitster.Core.Models;

namespace Gitster.Core.Git.LibGit2;

internal static class LibGit2DiffParser
{
    public static List<DiffLine> ParseUnifiedDiff(string? patchText)
    {
        var lines = new List<DiffLine>();
        if (string.IsNullOrEmpty(patchText))
            return lines;

        foreach (var raw in patchText.Replace("\r\n", "\n").Split('\n'))
        {
            if (raw.StartsWith("diff ", StringComparison.Ordinal) ||
                raw.StartsWith("index ", StringComparison.Ordinal) ||
                raw.StartsWith("--- ", StringComparison.Ordinal) ||
                raw.StartsWith("+++ ", StringComparison.Ordinal) ||
                raw.StartsWith("new file", StringComparison.Ordinal) ||
                raw.StartsWith("deleted file", StringComparison.Ordinal) ||
                raw.StartsWith("similarity", StringComparison.Ordinal) ||
                raw.StartsWith("rename ", StringComparison.Ordinal))
            {
                continue;
            }

            if (raw.Length == 0)
            {
                lines.Add(new DiffLine(DiffLineKind.Context, string.Empty));
                continue;
            }

            var kind = raw[0] switch
            {
                '@' => DiffLineKind.Hunk,
                '+' => DiffLineKind.Added,
                '-' => DiffLineKind.Removed,
                _ => DiffLineKind.Context,
            };
            lines.Add(new DiffLine(kind, raw));
        }

        return lines;
    }
}
