using System.IO;

namespace Gitster.Core.Features;

public sealed class GitIgnoreTemplateService
{
    private static readonly IReadOnlyDictionary<string, string[]> Templates =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["VisualStudio"] =
            [
                ".vs/",
                "bin/",
                "obj/",
                "*.user",
            ],
            ["Windows"] =
            [
                "Thumbs.db",
                "Desktop.ini",
            ],
        };

    public IReadOnlyList<string> TemplateNames => Templates.Keys.Order(StringComparer.OrdinalIgnoreCase).ToList();

    public string GetPreview(string templateName)
    {
        if (!Templates.TryGetValue(templateName, out var lines))
            throw new ArgumentException($"Unknown gitignore template: {templateName}", nameof(templateName));

        return string.Join(Environment.NewLine, new[] { $"# --- Gitster: {templateName} ---" }.Concat(lines));
    }

    public void AppendTemplate(string repoPath, string templateName)
    {
        if (!Templates.TryGetValue(templateName, out var lines))
            throw new ArgumentException($"Unknown gitignore template: {templateName}", nameof(templateName));

        var path = Path.Combine(repoPath, ".gitignore");
        var existing = File.Exists(path)
            ? File.ReadAllLines(path).ToList()
            : [];
        var existingSet = existing
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var additions = lines.Where(l => !existingSet.Contains(l.Trim())).ToList();
        if (additions.Count == 0)
            return;

        if (existing.Count > 0 && !string.IsNullOrWhiteSpace(existing[^1]))
            existing.Add(string.Empty);
        existing.Add($"# --- Gitster: {templateName} ---");
        existing.AddRange(additions);
        File.WriteAllLines(path, existing);
    }
}
