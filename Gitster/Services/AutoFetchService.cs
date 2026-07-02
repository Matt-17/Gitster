using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Gitster.Services.Git;
using Timer = System.Timers.Timer;

namespace Gitster.Services;

public partial class AutoFetchService : ObservableObject, IDisposable
{
    private readonly IGitBackend _git;
    private readonly RepositoryStateService? _stateService;
    private readonly Timer _timer;
    private const int DefaultIntervalSeconds = 60;
    private const int FailureBackoffSeconds = 300;
    private int _consecutiveFailures;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private int _intervalSeconds = DefaultIntervalSeconds;

    [ObservableProperty]
    private DateTime? _lastFetchAt;

    [ObservableProperty]
    private bool _isRemoteOperationRunning;

    public AutoFetchService(IGitBackend git, RepositoryStateService? stateService = null)
    {
        _git = git;
        _stateService = stateService;
        _timer = new Timer { AutoReset = true };
        _timer.Elapsed += async (_, _) => await TickAsync();
    }

    partial void OnIsEnabledChanged(bool value)
    {
        _timer.Interval = Math.Max(10, IntervalSeconds) * 1000;
        _timer.Enabled = value && !IsWindowMinimized();
    }

    partial void OnIntervalSecondsChanged(int value)
    {
        _timer.Interval = Math.Max(10, value) * 1000;
    }

    public void OnWindowStateChanged(WindowState state)
    {
        _timer.Enabled = IsEnabled && state != WindowState.Minimized;
    }

    private static bool IsWindowMinimized()
        => Application.Current.MainWindow?.WindowState == WindowState.Minimized;

    private async Task TickAsync()
        => await RunOnceAsync();

    public async Task RunOnceAsync()
    {
        if (IsRemoteOperationRunning || _stateService?.IsOperationRunning == true)
            return;

        try
        {
            await _git.FetchAsync();
            _consecutiveFailures = 0;
            IntervalSeconds = DefaultIntervalSeconds;
            SetLastFetchAt(DateTime.Now);
        }
        catch (Exception ex)
        {
            _consecutiveFailures++;
            if (_consecutiveFailures >= 3)
                IntervalSeconds = FailureBackoffSeconds;

            System.Diagnostics.Debug.WriteLine($"AutoFetch failed: {ex}");
        }
    }

    private void SetLastFetchAt(DateTime value)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            LastFetchAt = value;
            return;
        }

        _ = dispatcher.InvokeAsync(() => LastFetchAt = value);
    }

    public void Dispose() => _timer.Dispose();
}
