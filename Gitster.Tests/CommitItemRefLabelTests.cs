using Gitster.Services.Git;

namespace Gitster.Tests;

[TestClass]
public sealed class CommitItemRefLabelTests
{
    [TestMethod]
    public void RefLabelProjection_WhenCrowded_ShowsFirstThreeAndOverflowChip()
    {
        var labels = new[]
        {
            new CommitRefLabel("main", CommitRefKind.CurrentBranch, IsCurrent: true),
            new CommitRefLabel("feature/a", CommitRefKind.LocalBranch),
            new CommitRefLabel("feature/b", CommitRefKind.LocalBranch),
            new CommitRefLabel("origin/main", CommitRefKind.RemoteBranch),
            new CommitRefLabel("origin/feature/a", CommitRefKind.RemoteBranch),
        };

        var item = new CommitItem(
            "message",
            new DateTime(2026, 1, 1),
            "abc1234",
            "Tester",
            refLabels: labels);

        Assert.AreEqual(5, item.RefLabels.Count);
        CollectionAssert.AreEqual(labels.Take(3).ToArray(), item.VisibleRefLabels.ToArray());
        Assert.AreEqual(2, item.HiddenRefLabelCount);
        Assert.AreEqual("+2", item.HiddenRefLabelText);
        Assert.IsTrue(item.HasHiddenRefLabels);
        StringAssert.Contains(item.RefLabelsTooltip, "origin/feature/a");
    }

    [TestMethod]
    public void RefLabelProjection_WithThreeLabels_HasNoOverflowChip()
    {
        var labels = new[]
        {
            new CommitRefLabel("main", CommitRefKind.CurrentBranch, IsCurrent: true),
            new CommitRefLabel("feature/a", CommitRefKind.LocalBranch),
            new CommitRefLabel("origin/main", CommitRefKind.RemoteBranch),
        };

        var item = new CommitItem(
            "message",
            new DateTime(2026, 1, 1),
            "abc1234",
            "Tester",
            refLabels: labels);

        Assert.AreEqual(3, item.VisibleRefLabels.Count);
        Assert.AreEqual(0, item.HiddenRefLabelCount);
        Assert.AreEqual(string.Empty, item.HiddenRefLabelText);
        Assert.IsFalse(item.HasHiddenRefLabels);
    }
}
