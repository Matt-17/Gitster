using Gitster.Core.Search;

namespace Gitster.Tests;

[TestClass]
public sealed class CommitQueryTests
{
    private static bool Match(string query, string message, string author = "", string email = "", string sha = "")
        => CommitQuery.Parse(query).Matches(message, author, email, sha);

    [TestMethod]
    public void EmptyQuery_MatchesEverything()
    {
        Assert.IsTrue(CommitQuery.Parse("").IsEmpty);
        Assert.IsTrue(CommitQuery.Parse("   ").IsEmpty);
        Assert.IsTrue(Match("", "anything", "anyone"));
    }

    [TestMethod]
    public void BareToken_MatchesAnyField()
    {
        Assert.IsTrue(Match("fix", "fix bug", "Alice"));
        Assert.IsTrue(Match("alice", "fix bug", "Alice"));
        Assert.IsTrue(Match("example", "fix bug", "Alice", "alice@example.com"));
        Assert.IsTrue(Match("abc1", "fix bug", "Alice", "", "abc1234def"));
        Assert.IsFalse(Match("zzz", "fix bug", "Alice"));
    }

    [TestMethod]
    public void AuthorField_RestrictsToAuthor()
    {
        Assert.IsTrue(Match("author:alice", "fix bug", "Alice"));
        Assert.IsTrue(Match("author:example", "fix bug", "Alice", "alice@example.com"));
        // "bug" is in the message, not the author → no match when restricted to author
        Assert.IsFalse(Match("author:bug", "fix bug", "Alice"));
    }

    [TestMethod]
    public void MessageField_RestrictsToMessage()
    {
        Assert.IsTrue(Match("message:bug", "fix bug", "Alice"));
        Assert.IsFalse(Match("message:alice", "fix bug", "Alice"));
    }

    [TestMethod]
    public void ShaField_MatchesPrefixOnly()
    {
        Assert.IsTrue(Match("sha:abc", "m", "a", "", "abc1234"));
        // Not a prefix → no match (sha is prefix-matched, not substring)
        Assert.IsFalse(Match("sha:1234", "m", "a", "", "abc1234"));
    }

    [TestMethod]
    public void SpacesAreAnd()
    {
        // both substrings must be present somewhere
        Assert.IsTrue(Match("stash introduced", "StashKiller introduced"));
        Assert.IsFalse(Match("stash removed", "StashKiller introduced"));
    }

    [TestMethod]
    public void FieldPrefix_AppliesOnlyToNextToken()
    {
        // message:Stash  →  "Stash" in message AND "Killer" anywhere
        var q = CommitQuery.Parse("message:Stash Killer");
        Assert.AreEqual(2, q.Terms.Count);
        Assert.AreEqual(QueryField.Message, q.Terms[0].Field);
        Assert.AreEqual("Stash", q.Terms[0].Value);
        Assert.AreEqual(QueryField.Any, q.Terms[1].Field);
        Assert.AreEqual("Killer", q.Terms[1].Value);

        Assert.IsTrue(q.Matches("StashKiller introduced", "Killer Mike", "", ""));
        // "Killer" only present as the author → still matches (token is Any)
        Assert.IsTrue(q.Matches("Stash work", "Killer Mike", "", ""));
        // "Stash" missing from message → fails
        Assert.IsFalse(q.Matches("work", "Killer Mike", "", ""));
    }

    [TestMethod]
    public void QuotedValue_IsOneTermWithSpaces()
    {
        var q = CommitQuery.Parse("message:\"Stash Killer\"");
        Assert.AreEqual(1, q.Terms.Count);
        Assert.AreEqual(QueryField.Message, q.Terms[0].Field);
        Assert.AreEqual("Stash Killer", q.Terms[0].Value);

        Assert.IsTrue(q.Matches("the Stash Killer feature", "x", "", ""));
        // adjacency required: "StashKiller" (no space) does NOT contain "Stash Killer"
        Assert.IsFalse(q.Matches("StashKiller feature", "x", "", ""));
    }

    [TestMethod]
    public void QuotedAuthor_MatchesFullName()
    {
        Assert.IsTrue(Match("author:\"Max Mustermann\"", "msg", "Max Mustermann"));
        Assert.IsFalse(Match("author:\"Max Mustermann\"", "msg", "Max Other"));
    }

    [TestMethod]
    public void CaseInsensitive()
    {
        Assert.IsTrue(Match("FIX", "fix bug"));
        Assert.IsTrue(Match("author:ALICE", "m", "alice"));
    }

    [TestMethod]
    public void BeforeAfter_FilterByDate()
    {
        var q = CommitQuery.Parse("after:2026-01-01 before:2026-12-31");
        Assert.IsTrue(q.Matches("m", "a", "", "", new DateTime(2026, 6, 1)));
        Assert.IsFalse(q.Matches("m", "a", "", "", new DateTime(2025, 6, 1)));
        Assert.IsFalse(q.Matches("m", "a", "", "", new DateTime(2027, 1, 1)));
    }

    [TestMethod]
    public void BeforeAfter_IgnoredWhenNoDateProvided()
    {
        // With no commit date supplied, date terms don't exclude (used by callers without dates).
        Assert.IsTrue(Match("after:2026-01-01", "m", "a"));
    }
}
