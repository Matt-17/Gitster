using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gitster.Services;
using Gitster.Core;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Threading;

namespace Gitster.ViewModels;

public partial class TitleBarViewModel : BaseViewModel
{
    private readonly Action _browseFolder;
    private readonly Action<string> _openRepo;
    private readonly AutoFetchService _autoFetch;
    private readonly DispatcherTimer _timer;

    public RecentReposService RecentRepos { get; }
    public ObservableCollection<RecentRepositoryItemViewModel> RecentRepositoryItems { get; } = [];

    /// <summary>Pinned repos only — the "Pinned" section of the switch-repo dropdown (A4).</summary>
    public ObservableCollection<RecentRepositoryItemViewModel> PinnedItems { get; } = [];

    /// <summary>Recent repos excluding pinned — the "Recent" section of the switch-repo dropdown (A4).</summary>
    public ObservableCollection<RecentRepositoryItemViewModel> RecentItems { get; } = [];

    public TitleBarViewModel(
        AutoFetchService autoFetch,
        RecentReposService recentRepos,
        RepositoryCommandContext commandContext)
        : this(commandContext.BrowseFolder, commandContext.OpenRepositoryPath, autoFetch, recentRepos)
    {
    }

    public TitleBarViewModel(Action browseFolder, Action<string> openRepo, AutoFetchService autoFetch, RecentReposService recentRepos)
    {
        _browseFolder = browseFolder;
        _openRepo     = openRepo;
        _autoFetch    = autoFetch;
        RecentRepos   = recentRepos;

        AutoFetchEnabled = _autoFetch.IsEnabled;
        _autoFetch.PropertyChanged += (_, _) => RefreshAutoFetchInfo();
        RecentRepos.PropertyChanged += OnRecentReposPropertyChanged;
        RecentRepos.Entries.CollectionChanged += OnRecentReposChanged;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _timer.Tick += (_, _) => RefreshAutoFetchInfo();
        _timer.Start();

        RefreshRecentRepositoryItems();
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

    [ObservableProperty]
    public partial string CurrentRepositoryPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsRecentRepositoriesPopupOpen { get; set; }

    [ObservableProperty]
    public partial RecentRepositoryItemViewModel? SelectedRecentRepository { get; set; }

    public bool HasRecentRepositories => RecentRepositoryItems.Count > 0;
    public bool HasPinned => PinnedItems.Count > 0;
    public bool HasRecentOnly => RecentItems.Count > 0;

    partial void OnAutoFetchEnabledChanged(bool value)
    {
        _autoFetch.IsEnabled = value;
        RefreshAutoFetchInfo();
    }

    partial void OnCurrentRepositoryPathChanged(string value)
    {
        RefreshRecentRepositoryItems();
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
        CurrentRepositoryPath = string.Empty;
        IncomingCount = 0;
        OutgoingCount = 0;
        HasIncoming = false;
        HasOutgoing = false;
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        IsRecentRepositoriesPopupOpen = false;
        _browseFolder();
    }

    [RelayCommand]
    private void OpenRecentRepo(string path)
    {
        IsRecentRepositoriesPopupOpen = false;
        _openRepo(path);
    }

    [RelayCommand]
    private void OpenSelectedRecentRepo()
    {
        if (SelectedRecentRepository is null) return;
        OpenRecentRepo(SelectedRecentRepository.FullPath);
    }

    [RelayCommand]
    private void OpenRecentRepositoriesPopup()
    {
        RefreshRecentRepositoryItems();
        IsRecentRepositoriesPopupOpen = true;
    }

    [RelayCommand]
    private void CloseRecentRepositoriesPopup()
    {
        IsRecentRepositoriesPopupOpen = false;
    }

    [RelayCommand]
    private void PinRepo(string path) => RecentRepos.Pin(path);

    [RelayCommand]
    private void UnpinRepo(string path) => RecentRepos.Unpin(path);

    [RelayCommand]
    private void RemoveRecentRepo(string path) => RecentRepos.Remove(path);

    [RelayCommand]
    private void TogglePinRepo(string path)
    {
        if (RecentRepos.IsPinned(path))
            RecentRepos.Unpin(path);
        else
            RecentRepos.Pin(path);
    }

    private void OnRecentReposPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(RecentReposService.Entries)) return;

        RecentRepos.Entries.CollectionChanged += OnRecentReposChanged;
        RefreshRecentRepositoryItems();
    }

    private void OnRecentReposChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshRecentRepositoryItems();
    }

    private void RefreshRecentRepositoryItems()
    {
        var selectedPath = SelectedRecentRepository?.FullPath;
        var currentPath = CurrentRepositoryPath;

        var items = RecentRepos.GetPinned()
            .Concat(RecentRepos.GetRecent())
            .Select(entry => new RecentRepositoryItemViewModel(
                entry.FullPath,
                entry.DisplayName,
                entry.DisplayPath,
                entry.Pinned,
                IsSamePath(entry.FullPath, currentPath)))
            .ToList();

        RecentRepositoryItems.Clear();
        foreach (var item in items)
            RecentRepositoryItems.Add(item);

        PinnedItems.Clear();
        foreach (var item in items.Where(i => i.IsPinned))
            PinnedItems.Add(item);

        RecentItems.Clear();
        foreach (var item in items.Where(i => !i.IsPinned))
            RecentItems.Add(item);

        OnPropertyChanged(nameof(HasPinned));
        OnPropertyChanged(nameof(HasRecentOnly));

        SelectedRecentRepository = RecentRepositoryItems.FirstOrDefault(item =>
            IsSamePath(item.FullPath, selectedPath))
            ?? RecentRepositoryItems.FirstOrDefault(item => item.IsActive)
            ?? RecentRepositoryItems.FirstOrDefault();
        OnPropertyChanged(nameof(HasRecentRepositories));
    }

    private static bool IsSamePath(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left)
           && !string.IsNullOrWhiteSpace(right)
           && string.Equals(
               left.TrimEnd('\\', '/'),
               right.TrimEnd('\\', '/'),
               StringComparison.OrdinalIgnoreCase);

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

public sealed partial class RecentRepositoryItemViewModel(
    string fullPath,
    string displayName,
    string displayPath,
    bool isPinned,
    bool isActive) : ObservableObject
{
    public string FullPath { get; } = fullPath;
    public string DisplayName { get; } = displayName;
    public string DisplayPath { get; } = displayPath;
    public bool IsPinned { get; } = isPinned;
    public bool IsActive { get; } = isActive;
    public string PinTooltip => IsPinned ? "Unpin repository" : "Pin repository";
}
