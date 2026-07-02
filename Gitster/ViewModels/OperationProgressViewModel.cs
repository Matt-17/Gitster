using System.Windows.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

using Gitster.Models;

namespace Gitster.ViewModels;

public partial class OperationProgressViewModel : ObservableObject, IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly DateTime _startedAt = DateTime.Now;

    public OperationProgressViewModel(string title)
    {
        TitleText = title;
        StageText = title;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => RefreshElapsed();
        _timer.Start();
        RefreshElapsed();
    }

    public string TitleText { get; }

    public event EventHandler? CancelRequested;

    [ObservableProperty]
    public partial bool CanCancel { get; set; }

    [ObservableProperty]
    public partial bool IsCancelRequested { get; set; }

    [ObservableProperty]
    public partial string StageText { get; set; }

    [ObservableProperty]
    public partial string DetailText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ElapsedText { get; set; } = "0s";

    [ObservableProperty]
    public partial double ProgressValue { get; set; }

    [ObservableProperty]
    public partial double ProgressMaximum { get; set; } = 100;

    public void Report(OperationProgress progress)
    {
        StageText = progress.Stage;
        DetailText = progress.Detail;
        ProgressMaximum = progress.Maximum <= 0 ? 100 : progress.Maximum;
        ProgressValue = Math.Clamp(progress.Value, 0, ProgressMaximum);
    }

    public void Cancel()
    {
        if (!CanCancel || IsCancelRequested)
            return;

        IsCancelRequested = true;
        StageText = "Canceling";
        DetailText = "Stopping the Git operation...";
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshElapsed()
    {
        var elapsed = DateTime.Now - _startedAt;
        ElapsedText = elapsed.TotalMinutes < 1
            ? $"{Math.Max(0, (int)elapsed.TotalSeconds)}s"
            : $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds:D2}s";
    }

    public void Dispose() => _timer.Stop();
}
