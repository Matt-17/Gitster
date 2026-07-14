namespace Gitster.ApplicationLayer.Features;

public sealed record SubmoduleStatus(string Path, string Sha, bool IsInitialized, bool HasChanges);

public static class SubmoduleStatusParser
{
    public static IReadOnlyList<SubmoduleStatus> Parse(string output) =>
        output
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd())
            .Where(line => line.Length > 0)
            .Select(ParseLine)
            .ToList();

    private static SubmoduleStatus ParseLine(string line)
    {
        var state = line[0];
        var rest = line[1..].Trim();
        var parts = rest.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return new SubmoduleStatus(
            parts.ElementAtOrDefault(1)?.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0] ?? string.Empty,
            parts.ElementAtOrDefault(0) ?? string.Empty,
            state != '-',
            state is '+' or 'U');
    }
}
