namespace Gitster.Core.Git;

/// <summary>Timestamp construction for history rewrites.</summary>
public static class RewriteDate
{
    /// <summary>
    /// Combines an edited date/time (minute precision from the picker) with the
    /// original commit's seconds, in the current local UTC offset.
    /// </summary>
    public static DateTimeOffset Build(DateTime newDate, DateTime originalDate)
    {
        var offset = DateTimeOffset.Now.Offset;
        return new DateTimeOffset(
            newDate.Year,
            newDate.Month,
            newDate.Day,
            newDate.Hour,
            newDate.Minute,
            originalDate.Second,
            offset);
    }
}
