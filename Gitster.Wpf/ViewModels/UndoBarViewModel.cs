using System.Windows;
using System.Windows.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Gitster.Core.Models;
using Gitster.Services;
using Gitster.ApplicationLayer;
using Gitster.Core.Git;
using Gitster.Services.OperationsLog;
using Gitster.Views;

namespace Gitster.ViewModels;

public partial class UndoBarViewModel : BaseViewModel
{
    private readonly OperationsLogService _opsLog;
    private readonly IGitBackend _git;
    private readonly OperationFeedbackService _feedback;
    private readonly IWindowService _windowService;
    private DispatcherTimer? _timer;

    public event EventHandler? UndoCompleted;
    public Func<IProgress<OperationProgress>, Task>? AfterUndoAsync { get; set; }

    [ObservableProperty]
    public partial bool HasUndoableOperation { get; set; }

    [ObservableProperty]
    public partial string LastOperationText { get; set; } = "No operations yet";

    public UndoBarViewModel(OperationsLogService opsLog, IGitBackend git, OperationFeedbackService feedback, IWindowService? windowService = null)
    {
        _opsLog = opsLog;
        _git = git;
        _feedback = feedback;
        _windowService = windowService ?? new WindowService();

        _opsLog.Changed += (_, _) =>
        {
            UpdateDisplay();
            if (HasUndoableOperation)
                StartTimer();
            else
                _timer?.Stop();
        };

        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        var op = _opsLog.MostRecentActive;
        if (op is null)
        {
            // A7 — the ops-log line is always present, so the undo affordance is
            // discoverable even before any operation has run.
            HasUndoableOperation = false;
            LastOperationText = "No operations yet";
            return;
        }

        HasUndoableOperation = true;

        var elapsed = DateTimeOffset.Now - op.Timestamp;
        var ago = elapsed.TotalSeconds < 60
            ? $"{(int)elapsed.TotalSeconds}s ago"
            : elapsed.TotalMinutes < 60
                ? $"{(int)elapsed.TotalMinutes}m ago"
                : op.Timestamp.ToString("HH:mm");

        LastOperationText = $"Last: {op.Description} · {ago}";
    }

    private void StartTimer()
    {
        _timer?.Stop();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => UpdateDisplay();
        _timer.Start();
    }

    [RelayCommand]
    private async Task Undo()
    {
        var record = _opsLog.MostRecentActive;
        if (record is null) return;

        try
        {
            var plan = await RunProgressDialogAsync("Undo", async (progress, owner) =>
            {
                var prepared = await _opsLog.PrepareUndoAsync(record, _git, progress);
                if (prepared is not UndoPlan.Ready ready)
                    return prepared;

                var replayOnTop = false;
                if (ready.WouldDiscard.Count > 0)
                {
                    progress.Report(new OperationProgress(
                        "Waiting for confirmation",
                        "Choose how Gitster should handle commits above the undo target.",
                        36));

                    var confirm = await owner.Dispatcher.InvokeAsync(() =>
                    {
                        var dialog = new UndoConfirmationDialog(ready)
                        {
                            Owner = owner,
                        };
                        var accepted = dialog.ShowDialog() == true;
                        return accepted ? (Accepted: true, Replay: dialog.ReplayOnTop) : (Accepted: false, Replay: false);
                    });

                    if (!confirm.Accepted)
                        return new UndoPlan.Canceled();

                    replayOnTop = confirm.Replay;
                }

                var title = replayOnTop ? "Undo with replay" : "Undo";
                await _feedback.RunAsync(
                    title,
                    () => replayOnTop
                        ? _opsLog.ExecuteUndoWithReplayAsync(ready, _git, progress)
                        : _opsLog.ExecuteUndoAsync(ready, _git, progress));

                if (AfterUndoAsync is not null)
                {
                    progress.Report(new OperationProgress(
                        "Refreshing view",
                        "Updating the commit list.",
                        90));
                    await AfterUndoAsync(progress);
                }

                progress.Report(new OperationProgress(
                    "Done",
                    "Undo complete.",
                    100));

                if (AfterUndoAsync is null)
                    UndoCompleted?.Invoke(this, EventArgs.Empty);

                return ready;
            });

            if (plan is UndoPlan.NotAvailable na)
                _windowService.Info(na.Reason, "Cannot undo");
            else if (plan is UndoPlan.Expired exp)
                _windowService.Info(exp.Reason, "Undo expired");
        }
        catch (Exception ex)
        {
            _windowService.Error($"Undo failed: {ex.Message}", "Undo");
        }
    }

    private async Task<T> RunProgressDialogAsync<T>(
        string title,
        Func<IProgress<OperationProgress>, OperationProgressWindow, Task<T>> action)
    {
        var viewModel = new OperationProgressViewModel(title);
        var window = new OperationProgressWindow(viewModel);
        var progress = new Progress<OperationProgress>(viewModel.Report);

        Task<T>? task = null;
        window.ContentRendered += async (_, _) =>
        {
            if (task != null)
                return;

            await window.Dispatcher.InvokeAsync(
                () => { },
                DispatcherPriority.ApplicationIdle);

            task = action(progress, window);
            _ = task.ContinueWith(t =>
            {
                Application.Current.Dispatcher.BeginInvoke(() =>
                    window.Complete(t.Status == TaskStatus.RanToCompletion));
            }, CancellationToken.None);
        };

        _windowService.ShowDialog(window);

        if (task is null)
            throw new InvalidOperationException("Undo operation did not start.");

        return await task;
    }

    private async Task RunProgressDialogAsync(
        string title,
        Func<IProgress<OperationProgress>, OperationProgressWindow, Task> action)
    {
        await RunProgressDialogAsync<object?>(title, async (progress, window) =>
        {
            await action(progress, window);
            return null;
        });
    }

    [RelayCommand]
    private void OpenLog()
    {
        var window = new OperationsLogWindow(_opsLog, string.Empty);
        _windowService.ShowDialog(window);
    }
}
