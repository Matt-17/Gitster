using Gitster.Core.History;

namespace Gitster.Tests;

[TestClass]
public sealed class HistoryCacheValidatorTests
{
    [TestMethod]
    public void DecideOpenState_CurrentBranchHeadMoved_ValidatesCachedTail()
    {
        var validator = new HistoryCacheValidator();
        var context = Context("new-head", HistoryScope.CurrentBranch);
        var existing = new BranchState("old-head", null, IsComplete: true, CachedCount: 12);

        var decision = validator.DecideOpenState(context, existing, historyRowsNeedGraphUpgrade: false);

        Assert.IsTrue(decision.ValidateHeadChange);
        Assert.IsFalse(decision.ResetRows);
        Assert.IsTrue(decision.InitialIsComplete);
        Assert.AreEqual(12, decision.InitialCachedCount);
    }

    [TestMethod]
    public void DecideOpenState_AllBranchesFingerprintMoved_ResetsRows()
    {
        var validator = new HistoryCacheValidator();
        var context = Context("new-fingerprint", HistoryScope.AllBranches);
        var existing = new BranchState("old-fingerprint", null, IsComplete: true, CachedCount: 12);

        var decision = validator.DecideOpenState(context, existing, historyRowsNeedGraphUpgrade: false);

        Assert.IsTrue(decision.ResetRows);
        Assert.IsFalse(decision.ValidateHeadChange);
    }

    [TestMethod]
    public void DecideOpenState_MissingGraphColumns_ResetsBeforeHeadValidation()
    {
        var validator = new HistoryCacheValidator();
        var context = Context("new-head", HistoryScope.CurrentBranch);
        var existing = new BranchState("old-head", null, IsComplete: true, CachedCount: 12);

        var decision = validator.DecideOpenState(context, existing, historyRowsNeedGraphUpgrade: true);

        Assert.IsTrue(decision.ResetRows);
        Assert.IsFalse(decision.ValidateHeadChange);
    }

    private static HistoryContext Context(string headSha, HistoryScope scope) =>
        new(
            "repo",
            "C:/repo",
            "C:/repo/.git",
            scope == HistoryScope.AllBranches ? "scope:all-branches" : "main",
            headSha,
            null,
            scope);
}
