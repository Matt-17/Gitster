using CommunityToolkit.Mvvm.ComponentModel;

namespace Gitster;

public partial class CommitItem : ObservableObject
{
    [ObservableProperty]
    private string _message;

    [ObservableProperty]
    private string _date;

    [ObservableProperty]
    private string _commitId;

    public CommitItem(string message, string date, string commitId)
    {
        _message = message;
        _date = date;
        _commitId = commitId;
    }
}
