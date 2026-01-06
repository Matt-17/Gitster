using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Gitster;

public partial class CommitItem : ObservableObject
{
    [ObservableProperty]
    public partial string Message { get; set; }

    [ObservableProperty]
    public partial DateTime Date { get; set; }

    [ObservableProperty]
    public partial string CommitId { get; set; }

    [ObservableProperty]
    public partial string AuthorName { get; set; }

    public CommitItem(string message, DateTime date, string commitId, string authorName = "")
    {
        Message = message;
        Date = date;
        CommitId = commitId;
        AuthorName = authorName;
    }
}
