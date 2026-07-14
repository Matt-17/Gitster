namespace Gitster.Core.Models;

public record TimestampPreset(string Label, Func<DateTime> Resolve);
