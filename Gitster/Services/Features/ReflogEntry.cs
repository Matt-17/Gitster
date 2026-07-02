namespace Gitster.Services.Features;

public sealed record ReflogEntry(string Selector, string Sha, string Action, string Message, DateTimeOffset? Date)
{
    public static ReflogEntry Parse(string line)
    {
        var parts = line.Split('\t');
        return new ReflogEntry(
            parts.ElementAtOrDefault(0) ?? string.Empty,
            parts.ElementAtOrDefault(1) ?? string.Empty,
            parts.ElementAtOrDefault(2) ?? string.Empty,
            parts.ElementAtOrDefault(3) ?? string.Empty,
            DateTimeOffset.TryParse(parts.ElementAtOrDefault(4), out var date) ? date : null);
    }
}
