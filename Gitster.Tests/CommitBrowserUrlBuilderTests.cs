using Gitster.Core.Git;

namespace Gitster.Tests;

[TestClass]
public sealed class CommitBrowserUrlBuilderTests
{
    [TestMethod]
    public void TryBuild_GitHubHttps_ReturnsCommitUrl()
    {
        Assert.IsTrue(CommitBrowserUrlBuilder.TryBuild(
            "https://github.com/example/project.git",
            "abc123",
            out var url));

        Assert.AreEqual("https://github.com/example/project/commit/abc123", url);
    }

    [TestMethod]
    public void TryBuild_GitLabSsh_ReturnsCommitUrl()
    {
        Assert.IsTrue(CommitBrowserUrlBuilder.TryBuild(
            "git@gitlab.com:group/project.git",
            "abc123",
            out var url));

        Assert.AreEqual("https://gitlab.com/group/project/commit/abc123", url);
    }

    [TestMethod]
    public void TryBuild_BitbucketHttps_ReturnsCommitsUrl()
    {
        Assert.IsTrue(CommitBrowserUrlBuilder.TryBuild(
            "https://bitbucket.org/team/project.git",
            "abc123",
            out var url));

        Assert.AreEqual("https://bitbucket.org/team/project/commits/abc123", url);
    }

    [TestMethod]
    public void TryBuild_AzureDevOpsHttps_ReturnsCommitUrl()
    {
        Assert.IsTrue(CommitBrowserUrlBuilder.TryBuild(
            "https://dev.azure.com/org/project/_git/repo",
            "abc123",
            out var url));

        Assert.AreEqual("https://dev.azure.com/org/project/_git/repo/commit/abc123", url);
    }

    [TestMethod]
    public void TryBuild_AzureDevOpsSsh_ReturnsCommitUrl()
    {
        Assert.IsTrue(CommitBrowserUrlBuilder.TryBuild(
            "git@ssh.dev.azure.com:v3/org/project/repo",
            "abc123",
            out var url));

        Assert.AreEqual("https://dev.azure.com/org/project/_git/repo/commit/abc123", url);
    }
}
