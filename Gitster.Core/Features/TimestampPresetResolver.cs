namespace Gitster.Core.Features;

public static class TimestampPresetResolver
{
    public static DateTime Resolve(string preset, DateTime now)
    {
        var normalized = preset.Trim().ToLowerInvariant();
        if (normalized == "yesterday 09:00")
            return now.Date.AddDays(-1).AddHours(9);
        if (normalized == "last friday 17:30")
        {
            var daysBack = ((int)now.DayOfWeek - (int)DayOfWeek.Friday + 7) % 7;
            if (daysBack == 0)
                daysBack = 7;
            return now.Date.AddDays(-daysBack).AddHours(17).AddMinutes(30);
        }

        return DateTime.TryParse(preset, out var parsed) ? parsed : now;
    }
}
