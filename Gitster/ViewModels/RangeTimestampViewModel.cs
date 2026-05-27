using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Gitster.Services.Git;

namespace Gitster.ViewModels;

public record TimestampPreviewEntry(
    string Sha,
    string Message,
    DateTime OriginalDate,
    DateTime NewDate);

public partial class RangeTimestampViewModel : ObservableObject
{
    private readonly IGitBackend _git;
    private readonly List<CommitItem> _allCommits;

    public event Action? RewriteCompleted;

    [ObservableProperty]
    public partial ObservableCollection<CommitItem> AllCommits { get; set; }

    [ObservableProperty]
    public partial CommitItem? FromCommit { get; set; }

    [ObservableProperty]
    public partial CommitItem? ToCommit { get; set; }

    [ObservableProperty]
    public partial bool ShiftEarlier { get; set; } = true;

    /// <summary>Inverse of <see cref="ShiftEarlier"/> — used for the "Later" radio button binding.</summary>
    public bool ShiftLater
    {
        get => !ShiftEarlier;
        set => ShiftEarlier = !value;
    }

    [ObservableProperty]
    public partial int Amount { get; set; } = 2;

    [ObservableProperty]
    public partial string Unit { get; set; } = "Hours";

    public IReadOnlyList<string> UnitOptions { get; } = ["Hours", "Days"];

    [ObservableProperty]
    public partial bool UpdateAuthorTimestamp { get; set; } = true;

    [ObservableProperty]
    public partial bool UpdateCommitterTimestamp { get; set; } = true;

    [ObservableProperty]
    public partial List<TimestampPreviewEntry> Preview { get; set; } = [];

    [ObservableProperty]
    public partial bool HasUnsafeCommits { get; set; }

    [ObservableProperty]
    public partial bool IsRewriteEnabled { get; set; }

    public int PreviewCount => Preview.Count;

    public string RewriteButtonText => Preview.Count == 1
        ? "Rewrite 1 commit"
        : $"Rewrite {Preview.Count} commits";

    public RangeTimestampViewModel(IGitBackend git, List<CommitItem> commits)
    {
        _git = git;
        _allCommits = commits;
        AllCommits = new ObservableCollection<CommitItem>(commits);

        if (commits.Count > 0)
        {
            ToCommit   = commits[0];                              // HEAD (newest)
            FromCommit = commits[Math.Min(4, commits.Count - 1)]; // HEAD~4 or last
        }
    }

    partial void OnFromCommitChanged(CommitItem? value)     => RefreshPreview();
    partial void OnToCommitChanged(CommitItem? value)       => RefreshPreview();
    partial void OnAmountChanged(int value)                 => RefreshPreview();
    partial void OnUnitChanged(string value)                => RefreshPreview();
    partial void OnUpdateAuthorTimestampChanged(bool value) => RefreshPreview();
    partial void OnUpdateCommitterTimestampChanged(bool value) => RefreshPreview();

    partial void OnShiftEarlierChanged(bool value)
    {
        OnPropertyChanged(nameof(ShiftLater));
        RefreshPreview();
    }

    partial void OnPreviewChanged(List<TimestampPreviewEntry> value)
    {
        OnPropertyChanged(nameof(PreviewCount));
        OnPropertyChanged(nameof(RewriteButtonText));
    }

    private void RefreshPreview()
    {
        if (FromCommit == null || ToCommit == null || Amount <= 0)
        {
            Preview = [];
            IsRewriteEnabled = false;
            return;
        }

        // AllCommits is newest-first.
        // "To"   = newer end  (lower index)
        // "From" = older end  (higher index)
        int fromIdx = _allCommits.IndexOf(FromCommit);
        int toIdx   = _allCommits.IndexOf(ToCommit);

        if (fromIdx < 0 || toIdx < 0 || toIdx > fromIdx)
        {
            Preview = [];
            IsRewriteEnabled = false;
            return;
        }

        var range = _allCommits.GetRange(toIdx, fromIdx - toIdx + 1); // newest-first

        var delta = Unit == "Days"
            ? TimeSpan.FromDays(Amount)
            : TimeSpan.FromHours(Amount);

        if (ShiftEarlier) delta = -delta;

        var preview = range
            .Select(c => new TimestampPreviewEntry(c.CommitId, c.Message, c.Date, c.Date + delta))
            .ToList();

        Preview = preview;
        HasUnsafeCommits = range.Any(c => c.RemoteState == CommitRemoteState.OnRemote);
        IsRewriteEnabled = preview.Count > 0;
    }

    [RelayCommand]
    private async Task Rewrite()
    {
        if (!IsRewriteEnabled || Preview.Count == 0) return;

        var rewrites = Preview.Select(p =>
        {
            var offset = DateTimeOffset.Now.Offset;
            DateTimeOffset? newAuthorDate    = UpdateAuthorTimestamp
                ? new DateTimeOffset(p.NewDate, offset)
                : null;
            DateTimeOffset? newCommitterDate = UpdateCommitterTimestamp
                ? new DateTimeOffset(p.NewDate, offset)
                : null;

            return new CommitRewrite(p.Sha,
                NewAuthorDate: newAuthorDate,
                NewCommitterDate: newCommitterDate);
        });

        await _git.RewriteCommitsAsync(rewrites);
        RewriteCompleted?.Invoke();
    }
}
