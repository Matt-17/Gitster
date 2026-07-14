using System.Text.RegularExpressions;

namespace Gitster.Services.Git;

/// <summary>Parsing/formatting of the Git identity form "Name &lt;email&gt;".</summary>
public static partial class GitIdentityFormat
{
    [GeneratedRegex(@"^(.+?)\s*<([^>]*)>\s*$")]
    private static partial Regex IdentityPattern();

    /// <summary>
    /// Splits "Name &lt;email&gt;" into parts. Input without angle brackets is
    /// treated as a bare name; the email part is then empty.
    /// </summary>
    public static (string Name, string Email) Parse(string text)
    {
        var m = IdentityPattern().Match(text);
        return m.Success
            ? (m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim())
            : (text.Trim(), string.Empty);
    }

    public static string Format(string name, string email)
        => string.IsNullOrEmpty(email) ? name : $"{name} <{email}>";
}
