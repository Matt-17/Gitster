using Gitster.Services.Git;

namespace Gitster.Tests;

[TestClass]
public sealed class GitShaTests
{
    private const string Full = "0123456789abcdef0123456789abcdef01234567";

    [TestMethod]
    public void Short_FullSha_ReturnsSevenChars()
        => Assert.AreEqual("0123456", GitSha.Short(Full));

    [TestMethod]
    public void Short_AlreadyShortOrEmpty_ReturnsInput()
    {
        Assert.AreEqual("0123456", GitSha.Short("0123456"));
        Assert.AreEqual("abc", GitSha.Short("abc"));
        Assert.AreEqual(string.Empty, GitSha.Short(null));
        Assert.AreEqual(string.Empty, GitSha.Short(string.Empty));
    }

    [TestMethod]
    public void Matches_FullVsFull_ComparesExactly()
    {
        Assert.IsTrue(GitSha.Matches(Full, Full));
        Assert.IsFalse(GitSha.Matches(Full, "fedcba9876543210fedcba9876543210fedcba98"));
    }

    [TestMethod]
    public void Matches_LegacyShortRecordVsFullSha_MatchesOnPrefix()
    {
        Assert.IsTrue(GitSha.Matches("0123456", Full));
        Assert.IsTrue(GitSha.Matches(Full, "0123456"));
        Assert.IsFalse(GitSha.Matches("0123457", Full));
    }

    [TestMethod]
    public void Matches_EmptyOrNull_NeverMatches()
    {
        Assert.IsFalse(GitSha.Matches(string.Empty, Full));
        Assert.IsFalse(GitSha.Matches(null, Full));
        Assert.IsFalse(GitSha.Matches(string.Empty, string.Empty));
    }
}
