using System.Windows.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Gitster.Models;

namespace Gitster.ViewModels;

public partial class RepositoryLoadingViewModel : ObservableObject, IDisposable
{
    private readonly CancellationTokenSource _cancellation;
    private readonly DispatcherTimer _timer;
    private readonly DateTime _startedAt = DateTime.Now;

    public RepositoryLoadingViewModel(
        string repositoryPath,
        CancellationTokenSource cancellation,
        string title = "Loading repository")
    {
        RepositoryPath = repositoryPath;
        _cancellation = cancellation;
        TitleText = title;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => RefreshElapsed();
        _timer.Start();
        RefreshElapsed();
    }

    public string TitleText { get; }

    public string RepositoryPath { get; }

    [ObservableProperty]
    public partial string StageText { get; set; } = "Opening repository";

    [ObservableProperty]
    public partial string DetailText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CounterText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ElapsedText { get; set; } = "0s";

    [ObservableProperty]
    public partial double ProgressValue { get; set; }

    [ObservableProperty]
    public partial double ProgressMaximum { get; set; } = 100;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public partial bool CanCancel { get; set; } = true;

    [ObservableProperty]
    public partial bool IsCancelling { get; set; }

    public void Report(RepositoryLoadProgress progress)
    {
        StageText = progress.Stage;
        DetailText = progress.Detail;
        CounterText = progress.CounterText;
        ProgressMaximum = 100;
        ProgressValue = Math.Max(ProgressValue, ProgressPercent(progress));
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        if (!CanCancel)
            return;

        IsCancelling = true;
        CanCancel = false;
        StageText = "Canceling...";
        DetailText = "Restoring the previous repository.";
        _cancellation.Cancel();
    }

    private static double ProgressPercent(RepositoryLoadProgress progress)
    {
        if (progress.CommitCount.HasValue && progress.TotalCommitCount is > 0)
        {
            var ratio = Math.Clamp(
                progress.CommitCount.Value / (double)progress.TotalCommitCount.Value,
                0,
                1);

            return progress.Stage switch
            {
                "Loading history" => 20 + ratio * 55,
                _ => ratio * 100,
            };
        }

        return progress.Stage switch
        {
            "Opening repository" => 2,
            "Validating history cache" => 8,
            "Attaching repository services" => 12,
            "Reading HEAD" => 16,
            "Counting history" => CountingProgress(progress.CommitCount),
            "Loading full history" => 20,
            "Loading history" => 20,
            "Computing remote state" => 78,
            "Loading stashes" => 84,
            "Loading branches" => 88,
            "Loading worktrees" => 92,
            "Finalizing" => 98,
            _ => 5,
        };
    }

    private static double CountingProgress(int? count)
    {
        if (!count.HasValue || count.Value <= 0)
            return 18;

        return Math.Min(20, 18 + Math.Log10(count.Value + 1));
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
