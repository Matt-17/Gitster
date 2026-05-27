namespace Gitster.Models;

public record TimestampPreset(string Label, Func<DateTime> Resolve);
