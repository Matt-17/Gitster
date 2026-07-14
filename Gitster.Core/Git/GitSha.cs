namespace Gitster.Core.Git;

/// <summary>
/// SHA formatting helpers. Operations and comparisons use full 40-char SHAs;
/// truncation is for display only.
/// </summary>
public static class GitSha
{
    public const int ShortLength = 7;

    /// <summary>Returns the 7-char display form of a SHA (as-is when already short or empty).</summary>
    public static string Short(string? sha)
        => string.IsNullOrEmpty(sha) || sha.Length <= ShortLength ? sha ?? string.Empty : sha[..ShortLength];

    /// <summary>
    /// Prefix-tolerant SHA equality: true when one value is a prefix of the other
    /// (at least <see cref="ShortLength"/> chars). Needed because operation records
    /// persisted by older Gitster versions stored short SHAs.
    /// </summary>
    public static bool Matches(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return false;

        var min = Math.Min(a.Length, b.Length);
        if (min < ShortLength)
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        return string.Compare(a, 0, b, 0, min, StringComparison.OrdinalIgnoreCase) == 0;
    }
}
