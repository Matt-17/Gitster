namespace Gitster.Services.Search;

/// <summary>The field a query term is restricted to (or <see cref="Any"/> for a bare token).</summary>
public enum QueryField { Any, Author, Message, Sha }

/// <summary>A single parsed query term: a value plus the field it is restricted to.</summary>
public sealed record QueryTerm(QueryField Field, string Value);

/// <summary>
/// Parses and evaluates the Gitster commit-filter grammar (plan A8). The same parser
/// powers the inline Commits filter and the Phase-4 Search mode, so it is a pure,
/// allocation-light, unit-tested component with no UI or git dependencies.
///
/// Grammar (tokens separated by spaces, all AND-combined, case-insensitive):
///   foo                → matches if "foo" appears in any field (message, author, email, sha-prefix)
///   author:foo         → restrict to author name/email
///   message:foo        → restrict to message
///   sha:abc            → restrict to sha prefix
///   "foo bar"          → whole quoted string is one term (spaces included)
///   author:"Max M"     → field prefix + quoted value
/// A field prefix applies only to the immediately following token.
/// </summary>
public sealed class CommitQuery
{
    public IReadOnlyList<QueryTerm> Terms { get; }

    /// <summary>True when there are no terms — an empty query matches everything.</summary>
    public bool IsEmpty => Terms.Count == 0;

    private CommitQuery(IReadOnlyList<QueryTerm> terms) => Terms = terms;

    public static CommitQuery Parse(string? text)
    {
        var terms = new List<QueryTerm>();
        if (string.IsNullOrWhiteSpace(text))
            return new CommitQuery(terms);

        int i = 0, n = text.Length;
        while (i < n)
        {
            while (i < n && char.IsWhiteSpace(text[i])) i++;
            if (i >= n) break;

            // Optional "field:" prefix (only when the leading word is a known field
            // and the token is not itself a quoted string).
            var field = QueryField.Any;
            if (text[i] != '"')
            {
                int colon = -1;
                for (int j = i; j < n; j++)
                {
                    char c = text[j];
                    if (c == ':') { colon = j; break; }
                    if (char.IsWhiteSpace(c) || c == '"') break;
                }
                if (colon > i)
                {
                    var parsed = text[i..colon].ToLowerInvariant() switch
                    {
                        "author" => (QueryField?)QueryField.Author,
                        "message" => QueryField.Message,
                        "msg" => QueryField.Message,
                        "sha" => QueryField.Sha,
                        _ => null,
                    };
                    if (parsed.HasValue)
                    {
                        field = parsed.Value;
                        i = colon + 1;
                    }
                }
            }

            // Value: a quoted string (spaces preserved) or a single whitespace-delimited word.
            string value;
            if (i < n && text[i] == '"')
            {
                i++;
                int start = i;
                while (i < n && text[i] != '"') i++;
                value = text[start..i];
                if (i < n) i++; // consume closing quote
            }
            else
            {
                int start = i;
                while (i < n && !char.IsWhiteSpace(text[i])) i++;
                value = text[start..i];
            }

            if (!string.IsNullOrEmpty(value))
                terms.Add(new QueryTerm(field, value));
        }

        return new CommitQuery(terms);
    }

    /// <summary>True when every term matches the given commit projection (AND semantics).</summary>
    public bool Matches(string message, string authorName, string authorEmail, string sha)
    {
        // Iterate by index to avoid enumerator allocation on the hot filtering path.
        for (int t = 0; t < Terms.Count; t++)
        {
            if (!MatchesTerm(Terms[t], message, authorName, authorEmail, sha))
                return false;
        }
        return true;
    }

    private static bool MatchesTerm(QueryTerm term, string message, string authorName, string authorEmail, string sha)
    {
        var v = term.Value;
        return term.Field switch
        {
            QueryField.Author => Contains(authorName, v) || Contains(authorEmail, v),
            QueryField.Message => Contains(message, v),
            QueryField.Sha => StartsWith(sha, v),
            _ => Contains(message, v) || Contains(authorName, v)
                 || Contains(authorEmail, v) || StartsWith(sha, v),
        };
    }

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static bool StartsWith(string haystack, string needle) =>
        haystack.StartsWith(needle, StringComparison.OrdinalIgnoreCase);
}
