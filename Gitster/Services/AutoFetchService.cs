using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Gitster.Services.Git;
using Timer = System.Timers.Timer;

namespace Gitster.Services;

public partial class AutoFetchService : ObservableObject, IDisposable
{
    private readonly IGitBackend _git;
    private readonly Timer _timer;
    private const int DefaultIntervalSeconds = 60;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private int _intervalSeconds = DefaultIntervalSeconds;

    [ObservableProperty]
    private DateTime? _lastFetchAt;

    public AutoFetchService(IGitBackend git)
    {
        _git = git;
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
    {
        try
        {
            await _git.FetchAsync();
            await Application.Current.Dispatcher.InvokeAsync(() => LastFetchAt = DateTime.Now);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AutoFetch failed: {ex}");
        }
    }

    public void Dispose() => _timer.Dispose();
}
