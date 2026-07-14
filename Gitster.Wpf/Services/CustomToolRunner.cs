using Gitster.Core.Models;
using Gitster.Core.Git;
using Gitster.Views;

using Gitster.ApplicationLayer;

namespace Gitster.Services;

public sealed record CustomToolRunContext(
    string? SelectedCommitSha,
    string? CurrentBranch);

public enum CustomToolRunOutcome
{
    Skipped,
    RepositoryMayHaveChanged,
}

public sealed class CustomToolRunner
{
    private readonly ICustomToolsService _customTools;
    private readonly IWindowService _windowService;
    private readonly OperationFeedbackService _feedbackService;
    private readonly SnapshotService _snapshotService;
    private readonly IGitBackend _gitBackend;

    public CustomToolRunner(
        ICustomToolsService customTools,
        IWindowService windowService,
        OperationFeedbackService feedbackService,
        SnapshotService snapshotService,
        IGitBackend gitBackend)
    {
        _customTools = customTools;
        _windowService = windowService;
        _feedbackService = feedbackService;
        _snapshotService = snapshotService;
        _gitBackend = gitBackend;
    }

    public IReadOnlyList<CustomTool> GetTools()
    {
        try
        {
            return _customTools.GetTools();
        }
        catch
        {
            return [];
        }
    }

    public async Task<CustomToolRunOutcome> RunAsync(
        CustomTool tool,
        CustomToolRunContext context,
        CancellationToken ct = default)
    {
        string? revision = null;
        if (tool.NeedsCommit)
        {
            if (string.IsNullOrEmpty(context.SelectedCommitSha))
            {
                _windowService.Info("Select a commit first - this tool needs one.", tool.Name);
                return CustomToolRunOutcome.Skipped;
            }

            revision = context.SelectedCommitSha;
        }

        string? args = null;
        if (!string.IsNullOrEmpty(tool.Prompt))
        {
            var input = new TextInputDialog
            {
                Title = tool.Name,
                Prompt = tool.Prompt!,
            };

            if (_windowService.ShowDialog(input) != true)
                return CustomToolRunOutcome.Skipped;

            args = input.Value;
        }

        var command = _customTools.Substitute(tool.Command, revision, args, context.CurrentBranch);

        if (!string.IsNullOrEmpty(tool.Confirm))
        {
            var prompt = $"{tool.Confirm}\n\nCommand:\n{command}";
            if (!_windowService.Confirm(prompt, tool.Name))
                return CustomToolRunOutcome.Skipped;
        }

        await _snapshotService.CaptureAsync(_gitBackend, $"Before tool: {tool.Name}");

        try
        {
            var result = await _feedbackService.RunAsync(tool.Name,
                () => _customTools.RunAsync(command, ct),
                r => r.Success ? "completed" : $"exit {r.ExitCode}");

            var dialog = new ToolResultDialog(tool.Name, result.ExitCode, result.Output);
            _windowService.ShowDialog(dialog);
            return CustomToolRunOutcome.RepositoryMayHaveChanged;
        }
        catch (Exception ex)
        {
            _windowService.Error($"Tool '{tool.Name}' failed:\n{ex.Message}", "Gitster");
            return CustomToolRunOutcome.Skipped;
        }
    }
}
