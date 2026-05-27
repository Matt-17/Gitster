using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Gitster.ViewModels;

public partial class TitleBarViewModel : BaseViewModel
{
    private readonly Action _browseFolder;

    public TitleBarViewModel(Action browseFolder)
    {
        _browseFolder = browseFolder;
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
}
