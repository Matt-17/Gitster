using System.Windows;
using System.Windows.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Gitster.Services;
using Gitster.Services.Git;
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

        UndoPlan plan;
        try
        {
            plan = await _opsLog.PrepareUndoAsync(record, _git);
        }
        catch (Exception ex)
        {
            _windowService.Warning($"Undo preparation failed: {ex.Message}", "Undo");
            return;
        }

        if (plan is UndoPlan.Ready ready)
        {
            // A.6 — when intermediate commits exist, ask user to Discard or Replay
            bool replayOnTop = false;
            if (ready.WouldDiscard.Count > 0)
            {
                var dialog = new UndoConfirmationDialog(ready);
                if (_windowService.ShowDialog(dialog) != true) return;
                replayOnTop = dialog.ReplayOnTop;
            }

            try
            {
                if (replayOnTop)
                    await _feedback.RunAsync("Undo (replay)", () => _opsLog.ExecuteUndoWithReplayAsync(ready, _git));
                else
                    await _feedback.RunAsync("Undo", () => _opsLog.ExecuteUndoAsync(ready, _git));
            }
            catch (Exception ex)
            {
                _windowService.Error($"Undo failed: {ex.Message}", "Undo");
            }
        }
        else if (plan is UndoPlan.NotAvailable na)
        {
            _windowService.Info(na.Reason, "Cannot undo");
        }
        else if (plan is UndoPlan.Expired exp)
        {
            _windowService.Info(exp.Reason, "Undo expired");
        }
    }

    [RelayCommand]
    private void OpenLog()
    {
        var window = new OperationsLogWindow(_opsLog, string.Empty);
        _windowService.ShowDialog(window);
    }
}
