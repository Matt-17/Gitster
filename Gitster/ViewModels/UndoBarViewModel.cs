using System.Windows.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Gitster.Services;

namespace Gitster.ViewModels;

public partial class UndoBarViewModel : BaseViewModel
{
    private readonly OperationsLog _log;
    private DispatcherTimer? _timer;

    [ObservableProperty]
    public partial bool HasUndoableOperation { get; set; }

    [ObservableProperty]
    public partial string LastOperationText { get; set; } = string.Empty;

    public UndoBarViewModel(OperationsLog log)
    {
        _log = log;
        _log.Changed += (s, e) =>
        {
            UpdateDisplay();
            if (HasUndoableOperation)
                StartTimer();
            else
                _timer?.Stop();
        };
    }

    private void UpdateDisplay()
    {
        var op = _log.Peek();
        if (op == null)
        {
            HasUndoableOperation = false;
            LastOperationText = string.Empty;
            return;
        }

        HasUndoableOperation = true;

        var elapsed = DateTime.Now - op.Timestamp;
        var ago = elapsed.TotalSeconds < 60
            ? $"{(int)elapsed.TotalSeconds}s ago"
            : elapsed.TotalMinutes < 60
                ? $"{(int)elapsed.TotalMinutes}m ago"
                : op.Timestamp.ToString("HH:mm");

        LastOperationText = $"Last action: {op.Description} · {ago}";
    }

    private void StartTimer()
    {
        _timer?.Stop();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (s, e) => UpdateDisplay();
        _timer.Start();
    }

    [RelayCommand]
    private async Task Undo()
    {
        var op = _log.Pop();
        if (op == null) return;
        try
        {
            await op.UndoAction();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Undo failed: {ex.Message}");
        }
        UpdateDisplay();
    }
}
