using System.Windows;
using System.Windows.Media;

namespace Gitster.Services;

/// <summary>
/// Checks whether WPF can actually load a font family. An installed but broken font (seen with
/// the variable Cascadia fonts on Windows 10) throws <see cref="UnauthorizedAccessException"/>
/// during layout instead of falling back to the next family in the list, which takes down
/// whole windows. Probe before using a family from settings or a fallback list.
/// </summary>
public static class FontProbe
{
    /// <summary>
    /// True when the family (or comma-separated fallback list) resolves to loadable glyphs
    /// for regular and bold text without throwing.
    /// </summary>
    public static bool IsUsable(string? familyName)
    {
        if (string.IsNullOrWhiteSpace(familyName))
            return false;

        try
        {
            var family = new FontFamily(familyName);
            foreach (var weight in new[] { FontWeights.Normal, FontWeights.SemiBold, FontWeights.Bold })
            {
                var typeface = new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal);
                typeface.TryGetGlyphTypeface(out _);
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>The first candidate that loads; the last candidate when none does.</summary>
    public static string FirstUsable(params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (IsUsable(candidate))
                return candidate;
        }

        return candidates[^1];
    }
}
