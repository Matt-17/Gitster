using System.IO;
using System.Windows;

using Gitster.Services;
using Gitster.Core;
using Gitster.Core.Git;
using Gitster.Core.OperationsLog;
using Gitster.ViewModels;
using Gitster.Views;

using NSubstitute;

namespace Gitster.Tests;

[STATestClass]
[DoNotParallelize]
public sealed class BranchesViewModelTests
{
    [STATestMethod]
    public async Task StitchHistoryCommand_ConfirmsExecutesLogsAndRefreshes()
    {
        const string beforeSha = "1111111111111111111111111111111111111111";
        const string sourceSha = "2222222222222222222222222222222222222222";
        const string mergeSha = "3333333333333333333333333333333333333333";
        const string squashSha = "4444444444444444444444444444444444444444";

        var git = Substitute.For<IGitBackend>();
        var window = Substitute.For<IWindowService>();
        var feedback = new OperationFeedbackService();
        var opsLog = new OperationsLogService();
        var refreshCount = 0;
        var preview = new HistoryStitchPreview(
            "old/history",
            sourceSha,
            "main",
            beforeSha,
            IsSourceAlreadyReachable: false,
            UniqueSourceCommitCount: 2,
            SquashMatchSha: squashSha,
            Warnings: [],
            Blocks: []);
        var result = new HistoryStitchResult(
            "old/history",
            sourceSha,
            "main",
            "backup/main-before-history-stitch-1111111-20260701010101",
            mergeSha);

        git.GetBranchListAsync().Returns(Task.FromResult<IReadOnlyList<BranchListItem>>(
            [
                Branch("main", beforeSha, isCurrent: true),
                Branch("old/history", sourceSha, isCurrent: false),
            ]));
        git.PreviewHistoryStitchAsync("old/history").Returns(Task.FromResult(preview));
        git.GetHeadShaAsync().Returns(Task.FromResult(beforeSha));
        git.StitchHistoryAsync("old/history").Returns(Task.FromResult(result));
        window.ShowDialog(Arg.Any<Window>()).Returns(call => call.Arg<Window>() is HistoryStitchDialog);

        var vm = new BranchesViewModel(
            git,
            feedback,
            opsLog,
            new SnapshotService(),
            new SourceArchiveService(git, window, feedback),
            CreatePreferences(),
            window,
            () =>
            {
                refreshCount++;
                return Task.CompletedTask;
            },
            dialogs: new WpfDialogService(window));

        await vm.LoadAsync();
        vm.SelectedBranch = vm.Branches.Single(b => b.Name == "old/history");

        await vm.StitchHistoryCommand.ExecuteAsync(null);

        await git.Received(1).PreviewHistoryStitchAsync("old/history");
        await git.Received(1).StitchHistoryAsync("old/history");
        window.Received(1).ShowDialog(Arg.Is<Window>(w =>
            w.GetType() == typeof(HistoryStitchDialog)
            && ReferenceEquals(((HistoryStitchDialog)w).Preview, preview)));
        window.Received(1).Info(
            Arg.Is<string>(s =>
                s.Contains("Backup branch", StringComparison.Ordinal)
                && s.Contains("git revert -m 1 3333333", StringComparison.Ordinal)),
            "History stitched");

        Assert.AreEqual(1, refreshCount);
        Assert.AreEqual(1, opsLog.Records.Count);
        var record = opsLog.Records[0];
        Assert.AreEqual(OperationKind.Merge, record.Kind);
        Assert.AreEqual("Stitch history from old/history", record.Description);
        Assert.AreEqual("main", record.BranchName);
        // Records store full SHAs; truncation is display-only (BeforeShaShort/AfterShaShort).
        Assert.AreEqual(beforeSha, record.BeforeSha);
        Assert.AreEqual(mergeSha, record.AfterSha);
        Assert.AreEqual("1111111", record.BeforeShaShort);
        Assert.AreEqual("3333333", record.AfterShaShort);
    }

    private static BranchListItem Branch(string name, string sha, bool isCurrent) =>
        new(
            name,
            UpstreamName: null,
            TipSha: sha,
            TipMessage: $"{name} tip",
            LastActivity: DateTimeOffset.Now,
            Ahead: 0,
            Behind: 0,
            IsCurrent: isCurrent,
            IsRemote: false,
            IsMerged: false);

    private static UiPreferencesService CreatePreferences()
    {
        var dir = Path.Combine(Path.GetTempPath(), "Gitster.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return new UiPreferencesService(Path.Combine(dir, "ui-settings.json"));
    }
}
