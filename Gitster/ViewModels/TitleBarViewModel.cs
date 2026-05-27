using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gitster.Services;
using System.Windows.Threading;

namespace Gitster.ViewModels;

public partial class TitleBarViewModel : BaseViewModel
{
    private readonly Action _browseFolder;
    private readonly AutoFetchService _autoFetch;
    private readonly DispatcherTimer _timer;

    public TitleBarViewModel(Action browseFolder, AutoFetchService autoFetch)
    {
        _browseFolder = browseFolder;
        _autoFetch = autoFetch;

        AutoFetchEnabled = _autoFetch.IsEnabled;
        _autoFetch.PropertyChanged += (_, _) => RefreshAutoFetchInfo();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _timer.Tick += (_, _) => RefreshAutoFetchInfo();
        _timer.Start();

        RefreshAutoFetchInfo();
    }

    [ObservableProperty]
    public partial string RepositoryName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CurrentBranch { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int IncomingCount { get; set; }

    [ObservableProperty]
    public partial int OutgoingCount { get; set; }

    [ObservableProperty]
    public partial bool HasIncoming { get; set; }

    [ObservableProperty]
    public partial bool HasOutgoing { get; set; }

    [ObservableProperty]
    public partial bool AutoFetchEnabled { get; set; }

    [ObservableProperty]
    public partial string AutoFetchTooltip { get; set; } = "Auto-fetch: off";

    partial void OnAutoFetchEnabledChanged(bool value)
    {
        _autoFetch.IsEnabled = value;
        RefreshAutoFetchInfo();
    }

    public void UpdateStatus(string branch, string repoName, int incoming, int outgoing)
    {
        CurrentBranch = branch;
        RepositoryName = repoName;
        IncomingCount = incoming;
        OutgoingCount = outgoing;
        HasIncoming = incoming > 0;
        HasOutgoing = outgoing > 0;
    }

    public void Clear()
    {
        CurrentBranch = string.Empty;
        RepositoryName = string.Empty;
        IncomingCount = 0;
        OutgoingCount = 0;
        HasIncoming = false;
        HasOutgoing = false;
    }

    [RelayCommand]
    private void BrowseFolder() => _browseFolder();

    private void RefreshAutoFetchInfo()
    {
        if (!AutoFetchEnabled)
        {
            AutoFetchTooltip = "Auto-fetch: off";
            return;
        }

        var interval = _autoFetch.IntervalSeconds;
        if (_autoFetch.LastFetchAt is not DateTime last)
        {
            AutoFetchTooltip = $"Auto-fetch: every {interval}s";
            return;
        }

        var age = DateTime.Now - last;
        var ageText = age.TotalMinutes < 1
            ? "just now"
            : age.TotalHours < 1
                ? $"{Math.Max(1, (int)age.TotalMinutes)}m ago"
                : $"{(int)age.TotalHours}h ago";

        AutoFetchTooltip = $"Auto-fetch: every {interval}s · last fetched {ageText}";
    }
}
