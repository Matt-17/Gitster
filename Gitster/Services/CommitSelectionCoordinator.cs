using System.ComponentModel;

using Gitster.ViewModels;

namespace Gitster.Services;

public sealed class CommitSelectionCoordinator
{
    private readonly ISelectionContext _selectionContext;
    private readonly HistoryRewriteDraftViewModel _historyRewriteDraft;
    private readonly QuickActionsViewModel _quickActions;
    private readonly AuthorPanelViewModel _authorPanel;
    private CommitListViewModel? _commitList;

    public CommitSelectionCoordinator(
        ISelectionContext selectionContext,
        HistoryRewriteDraftViewModel historyRewriteDraft,
        QuickActionsViewModel quickActions,
        AuthorPanelViewModel authorPanel)
    {
        _selectionContext = selectionContext;
        _historyRewriteDraft = historyRewriteDraft;
        _quickActions = quickActions;
        _authorPanel = authorPanel;
    }

    public event Action<CommitItem?>? SelectedCommitChanged;
    public event Action? SelectedCommitsChanged;

    public void Attach(CommitListViewModel commitList)
    {
        if (_commitList is not null)
            _commitList.PropertyChanged -= OnCommitListPropertyChanged;

        _commitList = commitList;
        _commitList.PropertyChanged += OnCommitListPropertyChanged;
        ApplySelectedCommit(commitList.SelectedCommit);
        ApplySelectedCommits(commitList.SelectedCommits);
        _historyRewriteDraft.SetCommits(commitList.LoadedCommits);
    }

    public void Clear()
    {
        _selectionContext.SelectedCommit = null;
        _selectionContext.SelectedCommits = [];
        _historyRewriteDraft.SetSelectedCommit(null);
        _historyRewriteDraft.SetCommits([]);
        _quickActions.NotifySelectionChanged();
        SelectedCommitChanged?.Invoke(null);
        SelectedCommitsChanged?.Invoke();
    }

    private void OnCommitListPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_commitList is null)
            return;

        if (e.PropertyName == nameof(CommitListViewModel.SelectedCommit))
        {
            ApplySelectedCommit(_commitList.SelectedCommit);
        }
        else if (e.PropertyName == nameof(CommitListViewModel.SelectedCommits))
        {
            ApplySelectedCommits(_commitList.SelectedCommits);
        }
        else if (e.PropertyName == nameof(CommitListViewModel.LoadedCommits))
        {
            _historyRewriteDraft.SetCommits(_commitList.LoadedCommits);
        }
    }

    private void ApplySelectedCommit(CommitItem? commit)
    {
        _selectionContext.SelectedCommit = commit;
        _historyRewriteDraft.SetSelectedCommit(commit);
        _quickActions.NotifySelectionChanged();
        _ = _authorPanel.LoadFromCommitAsync(commit);
        SelectedCommitChanged?.Invoke(commit);
    }

    private void ApplySelectedCommits(IReadOnlyList<CommitItem> commits)
    {
        _selectionContext.SelectedCommits = commits;
        _quickActions.NotifySelectionChanged();
        SelectedCommitsChanged?.Invoke();
    }
}
