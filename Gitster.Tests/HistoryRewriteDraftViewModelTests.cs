using Gitster.Core.Git;
using Gitster.Services;
using Gitster.Core;
using Gitster.Core.OperationsLog;
using Gitster.ViewModels;
using NSubstitute;
using System.IO;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace Gitster.Tests;

[TestClass]
public sealed class HistoryRewriteDraftViewModelTests
{
    [TestMethod]
    public void EditOlderCommit_MarksDirectAndNewerCommitsTransitive()
    {
        var newest = Item("c3", "3333333333333333333333333333333333333333");
        var middle = Item("c2", "2222222222222222222222222222222222222222");
        var oldest = Item("c1", "1111111111111111111111111111111111111111");
        var vm = new HistoryRewriteDraftViewModel();

        vm.SetCommits([newest, middle, oldest]);
        vm.SetSelectedCommit(middle);
        vm.MessageText = "c2 rewritten";

        Assert.IsTrue(middle.IsHistoryEditDirect);
        Assert.IsTrue(middle.HasPendingMessageChange);
        Assert.AreEqual("c2 rewritten", middle.DisplayMessage);
        Assert.IsTrue(newest.IsHistoryEditTransitive);
        Assert.IsFalse(oldest.HasHistoryEditOverlay);
        Assert.AreEqual(1, vm.DirectEditCount);
        Assert.AreEqual(1, vm.TransitiveRewriteCount);
        Assert.AreEqual(2, vm.AffectedRewriteCount);
    }

    [TestMethod]
    public void EditAffectingRemoteCommit_SetsRemoteRewriteRisk()
    {
        var newest = Item("c3", "3333333333333333333333333333333333333333", CommitRemoteState.OnRemote);
        var middle = Item("c2", "2222222222222222222222222222222222222222", CommitRemoteState.LocalOnly);
        var vm = new HistoryRewriteDraftViewModel();

        vm.SetCommits([newest, middle]);
        vm.SetSelectedCommit(middle);
        vm.AuthorName = "Alice";

        Assert.IsTrue(vm.HasRemoteRewriteRisk);
        Assert.IsFalse(vm.HasLocalOnlyCleanup);
        StringAssert.Contains(vm.SummaryText, "force-with-lease");
    }

    [TestMethod]
    public void IncomingCommit_CannotCreateDraft()
    {
        var incoming = Item("remote", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", CommitRemoteState.Incoming);
        var vm = new HistoryRewriteDraftViewModel();

        vm.SetCommits([incoming]);
        vm.SetSelectedCommit(incoming);
        vm.MessageText = "edited";

        Assert.IsFalse(vm.HasDrafts);
        Assert.IsFalse(incoming.HasHistoryEditOverlay);
        Assert.AreEqual(0, vm.BuildRewrites().Count);
    }

    [TestMethod]
    public void IncomingRows_AreNeverMarkedTransitive()
    {
        var incoming = Item("remote", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", CommitRemoteState.Incoming);
        var newest = Item("c3", "3333333333333333333333333333333333333333");
        var middle = Item("c2", "2222222222222222222222222222222222222222");
        var vm = new HistoryRewriteDraftViewModel();

        vm.SetCommits([incoming, newest, middle]);
        vm.SetSelectedCommit(middle);
        vm.MessageText = "c2 rewritten";

        Assert.IsFalse(incoming.HasHistoryEditOverlay);
        Assert.IsFalse(incoming.IsHistoryEditTransitive);
        Assert.IsTrue(newest.IsHistoryEditTransitive);
        Assert.AreEqual(1, vm.TransitiveRewriteCount);
        Assert.AreEqual(2, vm.AffectedRewriteCount);
    }

    [TestMethod]
    public void OrphanedPair_RemoteCopyWarnsButDoesNotIncreaseLocalRewriteCount()
    {
        var incoming = Item(
            "remote copy",
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            CommitRemoteState.Incoming,
            "2222222");
        var newest = Item("c3", "3333333333333333333333333333333333333333");
        var edited = Item(
            "c2",
            "2222222222222222222222222222222222222222",
            CommitRemoteState.LocalOnly,
            "aaaaaaa");
        var vm = new HistoryRewriteDraftViewModel();

        vm.SetCommits([incoming, newest, edited]);
        vm.SetSelectedCommit(edited);
        vm.AuthorName = "Alice";

        Assert.IsTrue(vm.HasRemoteRewriteRisk);
        Assert.IsFalse(incoming.HasHistoryEditOverlay);
        Assert.AreEqual(1, vm.TransitiveRewriteCount);
        Assert.AreEqual(2, vm.AffectedRewriteCount);
        StringAssert.Contains(vm.SummaryText, "Remote contains old copies");
    }

    [TestMethod]
    public void ResetSelected_RemovesOnlySelectedDraftAndRecomputesProjection()
    {
        var newest = Item("c3", "3333333333333333333333333333333333333333");
        var middle = Item("c2", "2222222222222222222222222222222222222222");
        var oldest = Item("c1", "1111111111111111111111111111111111111111");
        var vm = new HistoryRewriteDraftViewModel();

        vm.SetCommits([newest, middle, oldest]);
        vm.SetSelectedCommit(oldest);
        vm.MessageText = "c1 rewritten";
        vm.SetSelectedCommit(middle);
        vm.AuthorName = "Alice";

        Assert.AreEqual(2, vm.DirectEditCount);

        vm.ResetSelectedCommand.Execute(null);

        Assert.AreEqual(1, vm.DirectEditCount);
        Assert.IsTrue(middle.IsHistoryEditTransitive);
        Assert.IsTrue(oldest.IsHistoryEditDirect);
        Assert.IsTrue(newest.IsHistoryEditTransitive);
    }

    [TestMethod]
    public async Task FullMessageLoad_AfterAuthorOnlyEdit_DoesNotCreateMessageRewrite()
    {
        var sha = "2222222222222222222222222222222222222222";
        var row = Item("subject", sha);
        var git = Substitute.For<IGitBackend>();
        git.GetCommitAsync(sha).Returns(Task.FromResult(new CommitDetails(
            sha,
            "subject\n\nbody\n",
            new DateTime(2026, 1, 1, 12, 0, 0),
            "Tester",
            "tester@gitster.test")));

        var vm = new HistoryRewriteDraftViewModel(
            git,
            new OperationFeedbackService(),
            new OperationsLogService(),
            new SnapshotService(),
            Substitute.For<IWindowService>(),
            () => "master",
            _ => Task.CompletedTask);

        vm.SetCommits([row]);
        vm.SetSelectedCommit(row);
        vm.AuthorName = "Alice";

        for (var i = 0; i < 20 && vm.IsLoadingDetails; i++)
            await Task.Delay(10);

        var rewrite = vm.BuildRewrites().Single();
        Assert.IsNull(rewrite.NewMessage);
        Assert.AreEqual("Alice", rewrite.NewAuthorName);
    }

    [TestMethod]
    public async Task Apply_SelectedEditedCommit_VerifiesAndPassesReplacementToRefresh()
    {
        var oldHead = "3333333333333333333333333333333333333333";
        var oldSelected = "2222222222222222222222222222222222222222";
        var newHead = "cccccccccccccccccccccccccccccccccccccccc";
        var replacement = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        var headRow = Item("c3", oldHead);
        var selectedRow = Item("c2", oldSelected);
        var git = Substitute.For<IGitBackend>();
        var window = Substitute.For<IWindowService>();
        var rewritten = false;
        string? preferredSelection = null;

        git.GetHeadShaAsync().Returns(_ => Task.FromResult(rewritten ? newHead : oldHead));
        git.GetCommitAsync(Arg.Any<string>()).Returns(call =>
        {
            var sha = (string)call[0]!;
            return Task.FromResult(sha.Equals(replacement, StringComparison.OrdinalIgnoreCase)
                ? new CommitDetails(replacement, "c2 rewritten", new DateTime(2026, 1, 1, 12, 0, 0), "Tester", "tester@gitster.test")
                : new CommitDetails(sha, "c2", new DateTime(2026, 1, 1, 12, 0, 0), "Tester", "tester@gitster.test"));
        });
        git.GetCommitsAsync(Arg.Any<CommitFilter?>()).Returns(_ => Task.FromResult<IReadOnlyList<CommitInfo>>(
            rewritten
                ? [
                    Info("c3", newHead),
                    Info("c2 rewritten", replacement),
                    Info("c1", "1111111111111111111111111111111111111111")
                ]
                : [
                    Info("c3", oldHead),
                    Info("c2", oldSelected),
                    Info("c1", "1111111111111111111111111111111111111111")
                ]));
        git.RewriteCommitsAsync(Arg.Any<IEnumerable<CommitRewrite>>(), Arg.Any<string?>())
            .Returns(Task.CompletedTask)
            .AndDoes(_ => rewritten = true);
        git.GetReflogSelectorForHeadAsync().Returns(Task.FromResult("HEAD@{1}"));

        var vm = new HistoryRewriteDraftViewModel(
            git,
            new OperationFeedbackService(),
            new OperationsLogService(),
            new SnapshotService(),
            window,
            () => "master",
            sha =>
            {
                preferredSelection = sha;
                return Task.CompletedTask;
            });

        vm.SetCommits([headRow, selectedRow]);
        vm.SetSelectedCommit(selectedRow);
        vm.MessageText = "c2 rewritten";

        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.IsFalse(vm.HasDrafts);
        Assert.AreEqual(replacement, preferredSelection);
    }

    [TestMethod]
    public async Task Apply_WhenVerificationFails_KeepsDraftsVisible()
    {
        var oldHead = "3333333333333333333333333333333333333333";
        var oldSelected = "2222222222222222222222222222222222222222";
        var headRow = Item("c3", oldHead);
        var selectedRow = Item("c2", oldSelected);
        var git = Substitute.For<IGitBackend>();
        var window = Substitute.For<IWindowService>();
        var refreshed = false;

        git.GetHeadShaAsync().Returns(Task.FromResult(oldHead));
        git.GetCommitAsync(Arg.Any<string>()).Returns(call =>
        {
            var sha = (string)call[0]!;
            return Task.FromResult(new CommitDetails(sha, "c2", new DateTime(2026, 1, 1, 12, 0, 0), "Tester", "tester@gitster.test"));
        });
        git.GetCommitsAsync(Arg.Any<CommitFilter?>()).Returns(Task.FromResult<IReadOnlyList<CommitInfo>>(
            [
                Info("c3", oldHead),
                Info("c2", oldSelected),
                Info("c1", "1111111111111111111111111111111111111111")
            ]));
        git.RewriteCommitsAsync(Arg.Any<IEnumerable<CommitRewrite>>(), Arg.Any<string?>())
            .Returns(Task.CompletedTask);

        var vm = new HistoryRewriteDraftViewModel(
            git,
            new OperationFeedbackService(),
            new OperationsLogService(),
            new SnapshotService(),
            window,
            () => "master",
            _ =>
            {
                refreshed = true;
                return Task.CompletedTask;
            });

        vm.SetCommits([headRow, selectedRow]);
        vm.SetSelectedCommit(selectedRow);
        vm.MessageText = "c2 rewritten";

        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.HasDrafts);
        Assert.IsTrue(selectedRow.IsHistoryEditDirect);
        Assert.IsTrue(refreshed);
        window.Received().Error(Arg.Is<string>(s => s.Contains("still reachable")), "Gitster");
    }

    [TestMethod]
    public async Task Apply_NonSelectedReplacementMetadataMissing_DoesNotBlockSelectedEdit()
    {
        var oldHead = "3333333333333333333333333333333333333333";
        var oldOther = "2222222222222222222222222222222222222222";
        var oldSelected = "1111111111111111111111111111111111111111";
        var newHead = "cccccccccccccccccccccccccccccccccccccccc";
        var otherReplacement = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        var selectedReplacement = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var headRow = Item("c3", oldHead);
        var otherRow = Item("c2", oldOther);
        var selectedRow = Item("c1", oldSelected);
        var git = Substitute.For<IGitBackend>();
        var window = Substitute.For<IWindowService>();
        var rewritten = false;
        string? preferredSelection = null;

        git.GetHeadShaAsync().Returns(_ => Task.FromResult(rewritten ? newHead : oldHead));
        git.GetCommitAsync(Arg.Any<string>()).Returns(call =>
        {
            var sha = (string)call[0]!;
            var message = sha.Equals(selectedReplacement, StringComparison.OrdinalIgnoreCase)
                ? "c1 rewritten"
                : "unchanged metadata";
            return Task.FromResult(new CommitDetails(sha, message, new DateTime(2026, 1, 1, 12, 0, 0), "Tester", "tester@gitster.test"));
        });
        git.GetCommitsAsync(Arg.Any<CommitFilter?>()).Returns(_ => Task.FromResult<IReadOnlyList<CommitInfo>>(
            rewritten
                ? [
                    Info("c3", newHead),
                    Info("c2", otherReplacement),
                    Info("c1 rewritten", selectedReplacement)
                ]
                : [
                    Info("c3", oldHead),
                    Info("c2", oldOther),
                    Info("c1", oldSelected)
                ]));
        git.RewriteCommitsAsync(Arg.Any<IEnumerable<CommitRewrite>>(), Arg.Any<string?>())
            .Returns(Task.CompletedTask)
            .AndDoes(_ => rewritten = true);
        git.GetReflogSelectorForHeadAsync().Returns(Task.FromResult("HEAD@{1}"));

        var vm = new HistoryRewriteDraftViewModel(
            git,
            new OperationFeedbackService(),
            new OperationsLogService(),
            new SnapshotService(),
            window,
            () => "master",
            sha =>
            {
                preferredSelection = sha;
                return Task.CompletedTask;
            });

        vm.SetCommits([headRow, otherRow, selectedRow]);
        vm.SetSelectedCommit(otherRow);
        vm.AuthorName = "Hidden Stale Edit";
        vm.SetSelectedCommit(selectedRow);
        vm.MessageText = "c1 rewritten";

        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.IsFalse(vm.HasDrafts);
        Assert.AreEqual(selectedReplacement, preferredSelection);
        window.DidNotReceive().Error(Arg.Any<string>(), Arg.Any<string>());
    }

    [TestMethod]
    public void ApplyIcon_UsesButtonForegroundForDisabledState()
    {
        XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var doc = XDocument.Load(RepoFile("Gitster.Wpf", "Controls", "HistoryRewriteDraftPanel.xaml"));
        var applyButton = doc.Descendants(ns + "Button")
            .Single(b => (string?)b.Attribute("Command") == "{Binding ApplyCommand}");
        var checkPath = applyButton.Descendants(ns + "Path")
            .Single(p => (string?)p.Attribute("Data") == "M4,11 L8,15 L17,5");

        Assert.AreEqual(
            "{Binding Foreground, RelativeSource={RelativeSource AncestorType=Button}}",
            (string?)checkPath.Attribute("Stroke"));
    }

    [TestMethod]
    public void MultiSelect_IdenticalValuesShowValue_DifferingValuesAreMixed()
    {
        var a = Item("same", "1111111111111111111111111111111111111111", date: new DateTime(2026, 3, 1, 8, 30, 0));
        var b = Item("other", "2222222222222222222222222222222222222222", date: new DateTime(2026, 3, 1, 17, 45, 0));
        var vm = new HistoryRewriteDraftViewModel();

        vm.SetCommits([b, a]);
        vm.SetSelectedCommits([b, a]);

        // Author name/email are identical across both rows, the message and the time of day are not.
        Assert.IsFalse(vm.IsAuthorNameMixed);
        Assert.AreEqual("Tester", vm.AuthorName);
        Assert.IsTrue(vm.IsMessageMixed);
        Assert.AreEqual(string.Empty, vm.MessageText);
        Assert.IsFalse(vm.IsAuthorDateMixed);
        Assert.AreEqual(new DateTime(2026, 3, 1), vm.AuthorDatePart);
        Assert.IsTrue(vm.IsAuthorTimeMixed);
        Assert.IsNull(vm.AuthorTimePart);
    }

    [TestMethod]
    public void MultiSelect_MessageEdit_AppliesToEverySelectedCommit()
    {
        var a = Item("c1", "1111111111111111111111111111111111111111");
        var b = Item("c2", "2222222222222222222222222222222222222222");
        var vm = new HistoryRewriteDraftViewModel();

        vm.SetCommits([b, a]);
        vm.SetSelectedCommits([b, a]);
        vm.MessageText = "shared message";

        Assert.AreEqual(2, vm.DirectEditCount);
        CollectionAssert.AreEquivalent(
            new[] { "shared message", "shared message" },
            vm.BuildRewrites().Select(r => r.NewMessage).ToArray());
        Assert.IsFalse(vm.IsMessageMixed);
    }

    [TestMethod]
    public void MultiSelect_DateEdit_KeepsIndividualTimes()
    {
        var a = Item("c1", "1111111111111111111111111111111111111111", date: new DateTime(2026, 3, 1, 8, 30, 0));
        var b = Item("c2", "2222222222222222222222222222222222222222", date: new DateTime(2026, 3, 2, 17, 45, 0));
        var vm = new HistoryRewriteDraftViewModel();

        vm.SetCommits([b, a]);
        vm.SetSelectedCommits([b, a]);
        vm.AuthorDatePart = new DateTime(2026, 5, 20);

        var dates = vm.BuildRewrites()
            .ToDictionary(r => r.Sha, r => r.NewAuthorDate!.Value.DateTime, StringComparer.OrdinalIgnoreCase);
        Assert.AreEqual(new DateTime(2026, 5, 20, 8, 30, 0), dates[a.FullSha]);
        Assert.AreEqual(new DateTime(2026, 5, 20, 17, 45, 0), dates[b.FullSha]);
    }

    [TestMethod]
    public void MultiSelect_TimeEdit_KeepsIndividualDates()
    {
        var a = Item("c1", "1111111111111111111111111111111111111111", date: new DateTime(2026, 3, 1, 8, 30, 0));
        var b = Item("c2", "2222222222222222222222222222222222222222", date: new DateTime(2026, 3, 2, 17, 45, 0));
        var vm = new HistoryRewriteDraftViewModel();

        vm.SetCommits([b, a]);
        vm.SetSelectedCommits([b, a]);
        vm.AuthorTimePart = new DateTime(2000, 1, 1, 21, 5, 0);

        var dates = vm.BuildRewrites()
            .ToDictionary(r => r.Sha, r => r.NewAuthorDate!.Value.DateTime, StringComparer.OrdinalIgnoreCase);
        Assert.AreEqual(new DateTime(2026, 3, 1, 21, 5, 0), dates[a.FullSha]);
        Assert.AreEqual(new DateTime(2026, 3, 2, 21, 5, 0), dates[b.FullSha]);
    }

    [TestMethod]
    public void MultiSelect_AuthorDateEdit_LeavesCommitterTimestampsUntouched()
    {
        var a = Item("c1", "1111111111111111111111111111111111111111", date: new DateTime(2026, 3, 1, 8, 30, 0));
        var vm = new HistoryRewriteDraftViewModel();

        vm.SetCommits([a]);
        vm.SetSelectedCommits([a]);
        vm.AuthorDatePart = new DateTime(2026, 5, 20);

        Assert.IsNull(vm.BuildRewrites().Single().NewCommitterDate);
    }

    [TestMethod]
    public void SyncCommitterWithAuthor_On_DragsCommitterTimestampAlongPerCommit()
    {
        var a = Item("c1", "1111111111111111111111111111111111111111", date: new DateTime(2026, 3, 1, 8, 30, 0));
        var b = Item("c2", "2222222222222222222222222222222222222222", date: new DateTime(2026, 3, 2, 17, 45, 0));
        var vm = new HistoryRewriteDraftViewModel();

        vm.SetCommits([b, a]);
        vm.SetSelectedCommits([b, a]);
        vm.SyncCommitterWithAuthor = true;
        vm.AuthorDatePart = new DateTime(2026, 5, 20);

        var rewrites = vm.BuildRewrites().ToDictionary(r => r.Sha, r => r, StringComparer.OrdinalIgnoreCase);
        Assert.AreEqual(new DateTime(2026, 5, 20, 8, 30, 0), rewrites[a.FullSha].NewCommitterDate!.Value.DateTime);
        Assert.AreEqual(new DateTime(2026, 5, 20, 17, 45, 0), rewrites[b.FullSha].NewCommitterDate!.Value.DateTime);
    }

    [TestMethod]
    public void SyncCommitterWithAuthor_Off_LeavesCommitterTimestampAlone()
    {
        var a = Item("c1", "1111111111111111111111111111111111111111", date: new DateTime(2026, 3, 1, 8, 30, 0));
        var vm = new HistoryRewriteDraftViewModel();

        vm.SetCommits([a]);
        vm.SetSelectedCommits([a]);
        vm.AuthorDatePart = new DateTime(2026, 5, 20);

        Assert.IsFalse(vm.SyncCommitterWithAuthor);
        Assert.IsNull(vm.BuildRewrites().Single().NewCommitterDate);
    }

    [TestMethod]
    public void MultiSelect_WithIncomingCommit_DisablesEditor()
    {
        var local = Item("c1", "1111111111111111111111111111111111111111");
        var incoming = Item("c2", "2222222222222222222222222222222222222222", CommitRemoteState.Incoming);
        var vm = new HistoryRewriteDraftViewModel();

        vm.SetCommits([incoming, local]);
        vm.SetSelectedCommits([incoming, local]);
        vm.MessageText = "should not apply";

        Assert.IsFalse(vm.IsSelectedCommitEditable);
        Assert.IsFalse(vm.HasDrafts);
        Assert.AreEqual(0, vm.BuildRewrites().Count);
    }

    [TestMethod]
    public async Task MultiSelect_KeepFirstMessageLine_TrimsEachCommitToItsOwnSubject()
    {
        var shaA = "1111111111111111111111111111111111111111";
        var shaB = "2222222222222222222222222222222222222222";
        var a = Item("c1", shaA);
        var b = Item("c2", shaB);
        var git = Substitute.For<IGitBackend>();
        git.GetCommitAsync(Arg.Any<string>()).Returns(call =>
        {
            var sha = (string)call[0]!;
            var subject = sha == shaA ? "c1" : "c2";
            return Task.FromResult(new CommitDetails(
                sha,
                $"{subject}\n\nbody line\nmore body\n",
                new DateTime(2026, 1, 1, 12, 0, 0),
                "Tester",
                "tester@gitster.test"));
        });

        var vm = new HistoryRewriteDraftViewModel(
            git,
            new OperationFeedbackService(),
            new OperationsLogService(),
            new SnapshotService(),
            Substitute.For<IWindowService>(),
            () => "master",
            _ => Task.CompletedTask);

        vm.SetCommits([b, a]);
        vm.SetSelectedCommits([b, a]);

        for (var i = 0; i < 50 && vm.IsLoadingDetails; i++)
            await Task.Delay(10);

        vm.KeepFirstMessageLineCommand.Execute(null);

        var messages = vm.BuildRewrites().ToDictionary(r => r.Sha, r => r.NewMessage, StringComparer.OrdinalIgnoreCase);
        Assert.AreEqual("c1", messages[shaA]);
        Assert.AreEqual("c2", messages[shaB]);
    }

    [TestMethod]
    public void MultiSelect_ResetSelected_ClearsDraftsOfAllSelectedCommits()
    {
        var a = Item("c1", "1111111111111111111111111111111111111111");
        var b = Item("c2", "2222222222222222222222222222222222222222");
        var vm = new HistoryRewriteDraftViewModel();

        vm.SetCommits([b, a]);
        vm.SetSelectedCommits([b, a]);
        vm.AuthorName = "Alice";
        Assert.AreEqual(2, vm.DirectEditCount);

        vm.ResetSelectedCommand.Execute(null);

        Assert.IsFalse(vm.HasDrafts);
        Assert.AreEqual(0, vm.BuildRewrites().Count);
        Assert.AreEqual("Tester", vm.AuthorName);
    }

    private static CommitItem Item(
        string message,
        string sha,
        CommitRemoteState state = CommitRemoteState.LocalOnly,
        string? orphanedPairSha = null,
        DateTime? date = null)
        => new(
            message,
            date ?? new DateTime(2026, 1, 1, 12, 0, 0),
            sha[..7],
            "Tester",
            "tester@gitster.test",
            state,
            sha,
            orphanedPairSha);

    private static CommitInfo Info(string message, string sha)
        => new(
            sha[..7],
            message,
            new DateTime(2026, 1, 1, 12, 0, 0),
            "Tester",
            "tester@gitster.test",
            CommitRemoteState.LocalOnly,
            sha);

    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(CurrentSourceFile())!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Gitster.slnx")))
            dir = dir.Parent;

        Assert.IsNotNull(dir, "Could not find repository root.");
        return Path.Combine([dir.FullName, .. parts]);
    }

    private static string CurrentSourceFile([CallerFilePath] string path = "") => path;
}
