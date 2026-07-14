using System.Windows;

using Gitster.Core.Models;
using Gitster.Services;
using Gitster.Core;
using Gitster.Core.Git;
using Gitster.Views;

using Microsoft.Win32;
using NSubstitute;

using Gitster.ApplicationLayer.Ui;

namespace Gitster.Tests;

[STATestClass]
[DoNotParallelize]
public sealed class CustomToolRunnerTests
{
    [STATestMethod]
    public async Task RunAsync_MissingSelectedCommit_SkipsExecution()
    {
        var tools = new FakeCustomToolsService();
        var window = new RecordingWindowService();
        var runner = CreateRunner(tools, window);
        var tool = Tool(needsCommit: true);

        var outcome = await runner.RunAsync(tool, new CustomToolRunContext(null, "main"));

        Assert.AreEqual(CustomToolRunOutcome.Skipped, outcome);
        Assert.AreEqual(0, tools.RunCommands.Count);
        Assert.AreEqual(1, window.Infos.Count);
        StringAssert.Contains(window.Infos[0].Text, "Select a commit first");
    }

    [STATestMethod]
    public async Task RunAsync_PromptCanceled_SkipsExecution()
    {
        var tools = new FakeCustomToolsService();
        var window = new RecordingWindowService { PromptDialogResult = false };
        var runner = CreateRunner(tools, window);
        var tool = Tool(command: "echo $ARGS", prompt: "Arguments:");

        var outcome = await runner.RunAsync(tool, new CustomToolRunContext("abc123", "main"));

        Assert.AreEqual(CustomToolRunOutcome.Skipped, outcome);
        Assert.AreEqual(0, tools.SubstituteCalls);
        Assert.AreEqual(0, tools.RunCommands.Count);
    }

    [STATestMethod]
    public async Task RunAsync_ConfirmationCanceled_SkipsExecution()
    {
        var tools = new FakeCustomToolsService();
        var window = new RecordingWindowService { ConfirmResult = false };
        var runner = CreateRunner(tools, window);
        var tool = Tool(command: "echo $REVISION", confirm: "Run it?");

        var outcome = await runner.RunAsync(tool, new CustomToolRunContext("abc123", "main"));

        Assert.AreEqual(CustomToolRunOutcome.Skipped, outcome);
        Assert.AreEqual(1, tools.SubstituteCalls);
        Assert.AreEqual(0, tools.RunCommands.Count);
        Assert.AreEqual(1, window.Confirmations.Count);
    }

    [STATestMethod]
    public async Task RunAsync_Success_SubstitutesContextAndRequestsRefresh()
    {
        var tools = new FakeCustomToolsService();
        var window = new RecordingWindowService
        {
            PromptValue = "extra",
            ConfirmResult = true,
        };
        var runner = CreateRunner(tools, window);
        var tool = Tool(
            command: "tool $REVISION $ARGS $BRANCH",
            confirm: "Run it?",
            needsCommit: true,
            prompt: "Arguments:");

        var outcome = await runner.RunAsync(tool, new CustomToolRunContext("abc123", "main"));

        Assert.AreEqual(CustomToolRunOutcome.RepositoryMayHaveChanged, outcome);
        Assert.AreEqual(1, tools.RunCommands.Count);
        Assert.AreEqual("tool abc123 extra main", tools.RunCommands[0]);
        Assert.AreEqual("abc123", tools.LastRevision);
        Assert.AreEqual("extra", tools.LastArgs);
        Assert.AreEqual("main", tools.LastBranch);
        StringAssert.Contains(window.Confirmations[0].Text, "tool abc123 extra main");
        Assert.IsTrue(window.ShownDialogs.Any(d => d is ToolResultDialog));
    }

    [STATestMethod]
    public async Task RunAsync_Failure_ShowsErrorWithoutRefreshOutcome()
    {
        var tools = new FakeCustomToolsService
        {
            RunHandler = (_, _) => throw new InvalidOperationException("tool exploded"),
        };
        var window = new RecordingWindowService();
        var runner = CreateRunner(tools, window);
        var tool = Tool(command: "broken");

        var outcome = await runner.RunAsync(tool, new CustomToolRunContext("abc123", "main"));

        Assert.AreEqual(CustomToolRunOutcome.Skipped, outcome);
        Assert.AreEqual(1, tools.RunCommands.Count);
        Assert.AreEqual("broken", tools.RunCommands[0]);
        Assert.AreEqual(1, window.Errors.Count);
        StringAssert.Contains(window.Errors[0].Text, "tool exploded");
        Assert.IsFalse(window.ShownDialogs.Any(d => d is ToolResultDialog));
    }

    private static CustomToolRunner CreateRunner(
        FakeCustomToolsService tools,
        RecordingWindowService window)
    {
        EnsureApplication();
        return new CustomToolRunner(
            tools,
            window,
            new OperationFeedbackService(),
            new SnapshotService(),
            Substitute.For<IGitBackend>());
    }

    private static void EnsureApplication()
    {
        if (Application.Current is null)
            _ = new Application();
    }

    private static CustomTool Tool(
        string command = "tool",
        string? confirm = null,
        bool needsCommit = false,
        string? prompt = null) =>
        new("Test Tool", command, confirm, needsCommit, prompt, CustomToolScope.Repository);

    private sealed class FakeCustomToolsService : ICustomToolsService
    {
        public Func<string, CancellationToken, Task<ToolRunResult>> RunHandler { get; set; } =
            (_, _) => Task.FromResult(new ToolRunResult(0, "ok"));

        public bool RepositoryAvailable => true;

        public int SubstituteCalls { get; private set; }

        public string? LastRevision { get; private set; }

        public string? LastArgs { get; private set; }

        public string? LastBranch { get; private set; }

        public List<string> RunCommands { get; } = [];

        public void Attach(string repoPath)
        {
        }

        public void Detach()
        {
        }

        public IReadOnlyList<CustomTool> GetTools() => [];

        public List<CustomTool> GetEditableTools(CustomToolScope scope) => [];

        public void Save(CustomToolScope scope, IEnumerable<CustomTool> tools)
        {
        }

        public string Substitute(string command, string? revision, string? args, string? branch)
        {
            SubstituteCalls++;
            LastRevision = revision;
            LastArgs = args;
            LastBranch = branch;
            return command
                .Replace("$REVISION", revision ?? string.Empty, StringComparison.Ordinal)
                .Replace("$CUR", revision ?? string.Empty, StringComparison.Ordinal)
                .Replace("$ARGS", args ?? string.Empty, StringComparison.Ordinal)
                .Replace("$BRANCH", branch ?? string.Empty, StringComparison.Ordinal);
        }

        public Task<ToolRunResult> RunAsync(string command, CancellationToken ct = default)
        {
            RunCommands.Add(command);
            return RunHandler(command, ct);
        }
    }

    private sealed class RecordingWindowService : IWindowService
    {
        public bool PromptDialogResult { get; set; } = true;

        public string PromptValue { get; set; } = string.Empty;

        public bool ConfirmResult { get; set; } = true;

        public List<Window> ShownDialogs { get; } = [];

        public List<(string Text, string Caption)> Confirmations { get; } = [];

        public List<(string Text, string Caption)> Infos { get; } = [];

        public List<(string Text, string Caption)> Errors { get; } = [];

        public void SetOwner(Window owner)
        {
        }

        public bool? ShowDialog(Window dialog)
        {
            ShownDialogs.Add(dialog);
            if (dialog is TextInputDialog input)
            {
                input.Value = PromptValue;
                return PromptDialogResult;
            }

            return true;
        }

        public bool? ShowDialog(CommonDialog dialog) => true;

        public MessageBoxResult ShowMessage(
            string text,
            string caption,
            MessageBoxButton button = MessageBoxButton.OK,
            MessageBoxImage image = MessageBoxImage.None) =>
            MessageBoxResult.OK;

        public MessageResult Ask(
            string text,
            string caption,
            MessageButtons buttons = MessageButtons.Ok,
            MessageIcon icon = MessageIcon.None) =>
            MessageResult.Ok;

        public bool Confirm(string text, string caption)
        {
            Confirmations.Add((text, caption));
            return ConfirmResult;
        }

        public void Info(string text, string caption = "Gitster") =>
            Infos.Add((text, caption));

        public void Warning(string text, string caption = "Gitster")
        {
        }

        public void Error(string text, string caption = "Gitster") =>
            Errors.Add((text, caption));
    }
}
