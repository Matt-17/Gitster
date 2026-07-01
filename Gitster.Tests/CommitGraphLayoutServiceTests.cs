using Gitster.Services.Git;
using Gitster.Services.History;

namespace Gitster.Tests;

[TestClass]
public sealed class CommitGraphLayoutServiceTests
{
    [TestMethod]
    public void Layout_LinearHistory_UsesSingleLane()
    {
        var rows = Layout(
            Node("c", "b"),
            Node("b", "a"),
            Node("a"));

        Assert.AreEqual(0, rows["c"].NodeLane);
        Assert.AreEqual(0, rows["b"].NodeLane);
        Assert.AreEqual(0, rows["a"].NodeLane);
        Assert.IsTrue(rows.Values.All(r => r.LaneCount == 1));
        Assert.AreEqual(rows["c"].NodeColorIndex, rows["b"].NodeColorIndex);
        Assert.AreEqual(rows["b"].NodeColorIndex, rows["a"].NodeColorIndex);
        Assert.IsTrue(rows.Values.SelectMany(r => r.Edges).All(e => e.ColorIndex == rows["c"].NodeColorIndex));
    }

    [TestMethod]
    public void Layout_MergeCommit_KeepsSecondParentOnSideLane()
    {
        var rows = Layout(
            Node("m", "c", "f"),
            Node("c", "b"),
            Node("f", "e"),
            Node("e", "b"),
            Node("b", "a"),
            Node("a"));

        Assert.AreEqual(0, rows["m"].NodeLane);
        Assert.AreEqual(1, rows["f"].NodeLane);
        Assert.AreNotEqual(rows["m"].NodeColorIndex, rows["f"].NodeColorIndex);
        Assert.IsTrue(rows["m"].Edges.Any(e =>
            e.FromAnchor == CommitGraphAnchor.Center &&
            e.ToAnchor == CommitGraphAnchor.Bottom &&
            e.ToLane == 1));
    }

    [TestMethod]
    public void Layout_MultipleBranchTips_AppendsDisconnectedTipLane()
    {
        var rows = Layout(
            Node("d", "b"),
            Node("c", "b"),
            Node("b", "a"),
            Node("a"));

        Assert.AreEqual(0, rows["d"].NodeLane);
        Assert.AreEqual(1, rows["c"].NodeLane);
        Assert.AreEqual(rows["d"].NodeColorIndex, rows["b"].NodeColorIndex);
        Assert.AreNotEqual(rows["d"].NodeColorIndex, rows["c"].NodeColorIndex);
        Assert.IsTrue(rows["c"].Edges.Any(e => e.FromLane == 1 && e.ToLane == 0));
    }

    [TestMethod]
    public void Layout_FilteredRows_DoNotConnectToHiddenParents()
    {
        var rows = Layout(
            Node("c", "b"),
            Node("a"));

        Assert.IsFalse(rows["c"].Edges.Any(e => e.FromAnchor == CommitGraphAnchor.Center));
    }

    [TestMethod]
    public void Layout_CarriesRefLabelsToRows()
    {
        var label = new CommitRefLabel("feature/test", CommitRefKind.LocalBranch);

        var rows = Layout(new CommitGraphNode("c", [], [label]));

        Assert.AreEqual(1, rows["c"].RefLabels.Count);
        Assert.AreEqual("feature/test", rows["c"].RefLabels[0].Name);
    }

    private static IReadOnlyDictionary<string, CommitGraphRow> Layout(params CommitGraphNode[] nodes) =>
        new CommitGraphLayoutService().Layout(nodes);

    private static CommitGraphNode Node(string sha, params string[] parents) =>
        new(sha, parents, Array.Empty<CommitRefLabel>());
}
