using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Gitster.Core.Models;
using Gitster.Services;
using Gitster.Core;
using Gitster.Core.Features;
using Gitster.Core.Git;
using Gitster.Core.OperationsLog;
using Gitster.Core.Ui;

using LibGit2Sharp;

namespace Gitster.ViewModels;

/// <summary>UI wrapper around a <see cref="BranchListItem"/>.</summary>
public sealed class BranchRow
{
    public BranchRow(BranchListItem info) => Info = info;

    public BranchListItem Info { get; }

    public string  Name        => Info.Name;
    public bool    IsCurrent   => Info.IsCurrent;
    public bool    IsRemote    => Info.IsRemote;
    public bool    IsMerged    => Info.IsMerged;
    public string  TipMessage  => Info.TipMessage;
    public DateTimeOffset LastActivity => Info.LastActivity;
    public DateTime LastActivityLocal => Info.LastActivity.LocalDateTime;
    public string  ShortSha    => GitSha.Short(Info.TipSha);
    public string? UpstreamName => Info.UpstreamName;

    public string  GroupLabel  => Info.IsRemote ? "Remote" : "Local";
    public int     GroupOrder  => Info.IsRemote ? 1 : 0;

    public bool    ShowAhead   => Info.Ahead  > 0;
    public bool    ShowBehind  => Info.Behind > 0;
    public string  AheadText   => $"↑{Info.Ahead}";
    public string  BehindText  => $"↓{Info.Behind}";

    public string  UpstreamDisplay => string.IsNullOrEmpty(Info.UpstreamName) ? "no upstream" : Info.UpstreamName!;
}

/// <summary>A node in the branch tree (A13): either a path folder or a leaf branch.</summary>
public sealed class BranchTreeNode
{
    public BranchTreeNode(string name, BranchRow? row)
    {
        Name = name;
        Row = row;
    }

    public string Name { get; }
    public BranchRow? Row { get; }
    public ObservableCollection<BranchTreeNode> Children { get; } = [];

    public bool IsBranch => Row != null;
    public bool IsCurrent => Row?.IsCurrent ?? false;
    public string? BranchName => Row?.Name;
}

/// <summary>
/// A node in the title-bar branch picker: a "/"-path folder or a leaf branch, carrying its
/// favourite/pin state. Folder expansion is remembered via <see cref="BranchFavoritesService"/>.
/// </summary>
public sealed class BranchPickerNode : ObservableObject
{
    private readonly BranchFavoritesService? _favorites;
    private readonly string? _repoPath;
    private readonly string _folderPath;
    private bool _isExpanded;

    public BranchPickerNode(
        string name, BranchRow? row, bool isGlobalFavorite, bool isPinned,
        BranchFavoritesService? favorites = null, string? repoPath = null, string folderPath = "")
    {
        Name = name;
        Row = row;
        IsGlobalFavorite = isGlobalFavorite;
        IsPinned = isPinned;
        _favorites = favorites;
        _repoPath = repoPath;
        _folderPath = folderPath;
        _isExpanded = row is not null || (favorites?.IsFolderExpanded(repoPath, folderPath) ?? true);
    }

    public string Name { get; }
    public BranchRow? Row { get; }
    public ObservableCollection<BranchPickerNode> Children { get; } = [];

    public bool IsBranch => Row != null;
    public bool IsFolder => Row == null;
    public string? FullName => Row?.Name;
    public bool IsCurrent => Row?.IsCurrent ?? false;

    /// <summary>Nesting level in the "/"-folder tree.</summary>
    public int Depth { get; internal set; }

    /// <summary>
    /// Indent for the label only — the rows themselves stay flush left so the current-branch
    /// check marker lines up for every branch, nested or not.
    /// </summary>
    public System.Windows.Thickness LabelIndent => new(Depth * 14, 0, 0, 0);

    public bool IsGlobalFavorite { get; }
    public bool IsPinned { get; }
    public bool IsFavorite => IsGlobalFavorite || IsPinned;
    /// <summary>Global favourites are managed in Options and cannot be unpinned from the dropdown.</summary>
    public bool CanTogglePin => !IsGlobalFavorite;
    public string PinTooltip => IsGlobalFavorite ? "Global favourite — manage in File ▸ Options"
                              : IsPinned ? "Unpin branch" : "Pin branch";

    /// <summary>Two-way bound to the folder's expander; persisted per repository.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged();
            if (IsFolder) _favorites?.SetFolderExpanded(_repoPath, _folderPath, value);
        }
    }
}

public partial class BranchesViewModel : BaseViewModel
{
    private readonly IGitBackend              _git;
    private readonly OperationFeedbackService _feedback;
    private readonly OperationsLogService     _opsLog;
    private readonly SnapshotService          _snapshots;
    private readonly SourceArchiveService     _archiveService;
    private readonly UiPreferencesService     _ui;
    private readonly IUserInteraction         _windowService;
    private readonly IDialogService           _dialogs;
    private readonly BranchFavoritesService?  _favorites;
    private readonly Func<Task>               _onChanged;

    private List<BranchRow> _all = [];

    public ObservableCollection<BranchRow> Branches { get; } = [];

    /// <summary>Local branches only — backs the title-bar branch dropdown (plan A11).</summary>
    public ObservableCollection<BranchRow> LocalBranches { get; } = [];

    /// <summary>Branches grouped by Local/Remote for the section headers in the list.</summary>
    public ICollectionView GroupedBranches { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(CanCheckout))]
    [NotifyPropertyChangedFor(nameof(CanMerge))]
    [NotifyPropertyChangedFor(nameof(CanStitchHistory))]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    [NotifyCanExecuteChangedFor(nameof(CheckoutCommand))]
    [NotifyCanExecuteChangedFor(nameof(MergeCommand))]
    [NotifyCanExecuteChangedFor(nameof(StitchHistoryCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    [NotifyCanExecuteChangedFor(nameof(RenameCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateFromHereCommand))]
    [NotifyCanExecuteChangedFor(nameof(ArchiveCommand))]
    public partial BranchRow? SelectedBranch { get; set; }

    [ObservableProperty]
    public partial string FilterText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortByDateActive))]
    [NotifyPropertyChangedFor(nameof(SortByNameActive))]
    public partial bool SortByName { get; set; }

    [ObservableProperty]
    public partial bool HasBranches { get; set; }

    [ObservableProperty]
    public partial int LocalCount { get; set; }

    public bool HasSelection    => SelectedBranch != null;
    public bool CanCheckout     => SelectedBranch is { IsCurrent: false };
    public bool CanMerge        => SelectedBranch is { IsCurrent: false };
    public bool CanStitchHistory => SelectedBranch is { IsCurrent: false };
    public bool CanDelete       => SelectedBranch is { IsCurrent: false, IsRemote: false };
    public bool SortByDateActive => !SortByName;
    public bool SortByNameActive => SortByName;

    public BranchesViewModel(
        IGitBackend              git,
        OperationFeedbackService feedback,
        OperationsLogService     opsLog,
        SnapshotService          snapshots,
        SourceArchiveService     archiveService,
        UiPreferencesService     ui,
        IUserInteraction?        windowService,
        BranchFavoritesService   favorites,
        RepositoryCommandContext commandContext,
        IDialogService           dialogs)
        : this(
            git,
            feedback,
            opsLog,
            snapshots,
            archiveService,
            ui,
            windowService,
            commandContext.RefreshSidebarBadges,
            favorites,
            dialogs)
    {
    }

    public BranchesViewModel(
        IGitBackend              git,
        OperationFeedbackService feedback,
        OperationsLogService     opsLog,
        SnapshotService          snapshots,
        SourceArchiveService     archiveService,
        UiPreferencesService     ui,
        IUserInteraction?        windowService,
        Func<Task>               onChanged,
        BranchFavoritesService?  favorites = null,
        IDialogService?          dialogs = null)
    {
        _git       = git;
        _feedback  = feedback;
        _opsLog    = opsLog;
        _snapshots = snapshots;
        _archiveService = archiveService;
        _ui        = ui;
        _windowService = windowService ?? NullUserInteraction.Instance;
        _dialogs   = dialogs ?? NullDialogService.Instance;
        _favorites = favorites;
        _onChanged = onChanged;

        if (_favorites is not null)
            _favorites.Changed += RebuildPicker;

        ShowTree = ui.BranchTreeView;

        GroupedBranches = CollectionViewSource.GetDefaultView(Branches);
        GroupedBranches.GroupDescriptions.Add(new PropertyGroupDescription(nameof(BranchRow.GroupLabel)));
    }

    /// <summary>Branches arranged as a path-segment tree (plan A13). Built alongside the flat list.</summary>
    public ObservableCollection<BranchTreeNode> BranchTree { get; } = [];

    /// <summary>Favourite branches (global + per-repo pins) for the title-bar picker's top section.</summary>
    public ObservableCollection<BranchPickerNode> FavoriteBranches { get; } = [];

    /// <summary>All local branches as a "/"-folder tree for the title-bar picker.</summary>
    public ObservableCollection<BranchPickerNode> BranchPickerTree { get; } = [];

    [ObservableProperty]
    public partial bool HasFavoriteBranches { get; set; }

    /// <summary>Exposed so File ▸ Options can edit global favourites; null only in unit tests.</summary>
    public BranchFavoritesService? Favorites => _favorites;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFlat))]
    public partial bool ShowTree { get; set; }

    public bool ShowFlat => !ShowTree;

    partial void OnShowTreeChanged(bool value)
    {
        _ui.BranchTreeView = value;
        Rebuild();
    }

    public async Task LoadAsync()
    {
        try
        {
            var items = await _git.GetBranchListAsync();
            _all = items.Select(b => new BranchRow(b)).ToList();
        }
        catch
        {
            _all = [];
        }

        LocalCount = _all.Count(b => !b.IsRemote);
        Rebuild();
    }

    public void Clear()
    {
        _all = [];
        Branches.Clear();
        SelectedBranch = null;
        HasBranches = false;
        LocalCount = 0;
    }

    partial void OnFilterTextChanged(string value) => Rebuild();
    partial void OnSortByNameChanged(bool value) => Rebuild();

    private void Rebuild()
    {
        var query = FilterText.Trim();

        IEnumerable<BranchRow> filtered = _all;
        if (!string.IsNullOrEmpty(query))
            filtered = filtered.Where(b => b.Name.Contains(query, StringComparison.OrdinalIgnoreCase));

        // Local group first, then Remote; within each group sort by the chosen key.
        var ordered = filtered
            .OrderBy(b => b.GroupOrder)
            .ThenBy(b => SortByName ? 0 : 1)               // keep stable
            .ThenByDescending(b => SortByName ? DateTimeOffset.MinValue : b.LastActivity)
            .ThenBy(b => SortByName ? b.Name : string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var prev = SelectedBranch?.Name;

        Branches.Clear();
        foreach (var b in ordered) Branches.Add(b);

        SelectedBranch = Branches.FirstOrDefault(b => b.Name == prev)
                         ?? Branches.FirstOrDefault(b => b.IsCurrent)
                         ?? Branches.FirstOrDefault();

        HasBranches = Branches.Count > 0;

        // Local-only projection for the title-bar branch picker (current branch first).
        LocalBranches.Clear();
        foreach (var b in _all.Where(b => !b.IsRemote)
                              .OrderByDescending(b => b.IsCurrent)
                              .ThenByDescending(b => b.LastActivity))
            LocalBranches.Add(b);

        if (ShowTree)
            RebuildTree(ordered);

        RebuildPicker();
    }

    /// <summary>Rebuilds the title-bar picker's favourites list and "/"-folder tree from local branches.</summary>
    private void RebuildPicker()
    {
        var repoPath = _git.RepositoryPath;
        var locals = _all.Where(b => !b.IsRemote).ToList();

        BranchPickerNode Leaf(BranchRow row, string name) => new(
            name, row,
            isGlobalFavorite: _favorites?.IsGlobalFavorite(row.Name) ?? false,
            isPinned:         _favorites?.IsPinned(repoPath, row.Name) ?? false,
            favorites: _favorites, repoPath: repoPath);

        // Favourites section: global favourites first, then per-repo pins, each alphabetical.
        FavoriteBranches.Clear();
        foreach (var row in locals
                     .Select(r => Leaf(r, r.Name))
                     .Where(n => n.IsFavorite)
                     .OrderByDescending(n => n.IsGlobalFavorite)
                     .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase))
            FavoriteBranches.Add(row);
        HasFavoriteBranches = FavoriteBranches.Count > 0;

        // Full "/"-folder tree, alphabetical. Favourites are shown only in the section above,
        // so they are excluded here (which also prunes folders that would otherwise be empty).
        var roots = new List<BranchPickerNode>();
        var folders = new Dictionary<string, BranchPickerNode>(StringComparer.Ordinal);

        bool IsFavorite(BranchRow r) =>
            (_favorites?.IsGlobalFavorite(r.Name) ?? false) ||
            (_favorites?.IsPinned(repoPath, r.Name) ?? false);

        foreach (var row in locals
                     .Where(r => !IsFavorite(r))
                     .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase))
        {
            var segments = row.Name.Split('/');
            var path = string.Empty;
            IList<BranchPickerNode> level = roots;

            for (int i = 0; i < segments.Length; i++)
            {
                var isLeaf = i == segments.Length - 1;
                path = path.Length == 0 ? segments[i] : $"{path}/{segments[i]}";

                if (isLeaf)
                {
                    level.Add(Leaf(row, segments[i]));
                }
                else if (folders.TryGetValue(path, out var existing))
                {
                    level = existing.Children;
                }
                else
                {
                    var folder = new BranchPickerNode(segments[i], null, false, false, _favorites, repoPath, path);
                    level.Add(folder);
                    folders[path] = folder;
                    level = folder.Children;
                }
            }
        }

        SortPickerLevel(roots);
        BranchPickerTree.Clear();
        foreach (var node in roots)
            BranchPickerTree.Add(node);
    }

    /// <summary>Sorts a tree level (folders before branches, each alphabetical) and recurses.</summary>
    private static void SortPickerLevel(IList<BranchPickerNode> level, int depth = 0)
    {
        var sorted = level
            .OrderByDescending(n => n.IsFolder)
            .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        level.Clear();
        foreach (var node in sorted)
        {
            node.Depth = depth;
            level.Add(node);
            if (node.IsFolder)
                SortPickerLevel(node.Children, depth + 1);
        }
    }

    /// <summary>Toggles a per-repo pin (title-bar picker). Global favourites are locked (managed in Options).</summary>
    [RelayCommand]
    private void ToggleBranchPin(string? fullName)
    {
        if (string.IsNullOrEmpty(fullName)) return;
        if (_favorites is null || _favorites.IsGlobalFavorite(fullName)) return;
        _favorites.TogglePin(_git.RepositoryPath, fullName);
        // Service raises Changed → RebuildPicker refreshes pins.
    }

    /// <summary>Builds the path-segment tree from the filtered, ordered rows (A13).</summary>
    private void RebuildTree(IReadOnlyList<BranchRow> rows)
    {
        var roots = new List<BranchTreeNode>();
        var index = new Dictionary<string, BranchTreeNode>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            var segments = row.Name.Split('/');
            var path = string.Empty;
            IList<BranchTreeNode> level = roots;

            for (int i = 0; i < segments.Length; i++)
            {
                var isLeaf = i == segments.Length - 1;
                path = path.Length == 0 ? segments[i] : $"{path}/{segments[i]}";

                if (isLeaf)
                {
                    level.Add(new BranchTreeNode(segments[i], row));
                }
                else if (index.TryGetValue(path, out var existing))
                {
                    level = existing.Children;
                }
                else
                {
                    var folder = new BranchTreeNode(segments[i], null);
                    level.Add(folder);
                    index[path] = folder;
                    level = folder.Children;
                }
            }
        }

        BranchTree.Clear();
        foreach (var node in roots)
            BranchTree.Add(node);
    }

    [RelayCommand]
    private void ToggleTree() => ShowTree = !ShowTree;

    [RelayCommand]
    private void SortByDate() => SortByName = false;

    [RelayCommand]
    private void SortByNameToggle() => SortByName = true;

    [RelayCommand]
    private void ClearFilter() => FilterText = string.Empty;

    [RelayCommand(CanExecute = nameof(CanCheckout))]
    private Task Checkout() => SelectedBranch is { } row ? CheckoutRowAsync(row) : Task.CompletedTask;

    /// <summary>Checks out a branch by name (title-bar dropdown, A11). No-op if already current.</summary>
    [RelayCommand]
    private Task CheckoutNamed(string? name)
    {
        var row = _all.FirstOrDefault(b => b.Name == name);
        return row is { IsCurrent: false } ? CheckoutRowAsync(row) : Task.CompletedTask;
    }

    private async Task CheckoutRowAsync(BranchRow row)
    {
        try
        {
            await _feedback.RunAsync("Checkout", () => _git.CheckoutBranchAsync(row.Name));
            await AfterChangeAsync();
        }
        catch (CheckoutConflictException)
        {
            // Dirty working tree would be overwritten — offer stash-and-checkout.
            var choice = _windowService.Ask(
                "You have uncommitted changes that would be overwritten by this checkout.\n\n" +
                "Stash your changes and switch anyway?",
                "Uncommitted changes",
                MessageButtons.OkCancel, MessageIcon.Warning);
            if (choice != MessageResult.Ok) return;

            try
            {
                await _feedback.RunAsync("Stash & checkout", async () =>
                {
                    await _git.CreateStashAsync($"Auto-stash before checkout of {row.Name}", includeUntracked: true);
                    await _git.CheckoutBranchAsync(row.Name);
                });
                await AfterChangeAsync();
                _windowService.Info(
                    "Your changes were stashed before switching. Find them in the Stashes view.",
                    "Stashed");
            }
            catch (Exception ex)
            {
                _windowService.Warning(ex.Message, "Checkout failed");
            }
        }
        catch (Exception ex)
        {
            _windowService.Warning(ex.Message, "Checkout failed");
        }
    }

    [RelayCommand(CanExecute = nameof(CanMerge))]
    private async Task Merge()
    {
        if (SelectedBranch is not { } row) return;

        BranchInfo current;
        try
        {
            current = await _git.GetCurrentBranchAsync();
        }
        catch (Exception ex)
        {
            _windowService.Warning($"Could not read the current branch:\n{ex.Message}", "Merge failed");
            return;
        }

        var strategy = _dialogs.MergeBranch(row.Name, current.Name);
        if (strategy is null) return;

        try
        {
            var beforeSha = await _git.GetHeadShaAsync();
            await _snapshots.CaptureAsync(_git, $"Merge {row.Name}");

            var result = await _feedback.RunAsync(
                "Merge",
                () => _git.MergeBranchAsync(row.Name, strategy.Value),
                DescribeMergeOutcome);

            if (result.Outcome != BranchMergeOutcome.UpToDate)
            {
                await _opsLog.RecordAsync(new OperationRecord(
                    Id:             Guid.NewGuid().ToString(),
                    Timestamp:      DateTimeOffset.Now,
                    Kind:           OperationKind.Merge,
                    Description:    $"Merge {row.Name} ({DescribeStrategy(strategy.Value)})",
                    BranchName:     result.TargetBranch,
                    BeforeSha:      beforeSha,
                    AfterSha:       result.HeadSha,
                    ReflogSelector: null,
                    Status:         OperationStatus.Active));
            }

            await AfterChangeAsync();
        }
        catch (CheckoutConflictException)
        {
            _windowService.Warning(
                "You have uncommitted changes that would be overwritten by this merge.",
                "Merge failed");
        }
        catch (GitConflictException ex)
        {
            await AfterChangeAsync();
            await ConflictGuidanceService.ShowIfConflictAsync(_dialogs, _windowService, _git, "Merge", ex);
        }
        catch (Exception ex)
        {
            _windowService.Warning(ex.Message, "Merge failed");
        }
    }

    [RelayCommand(CanExecute = nameof(CanStitchHistory))]
    private async Task StitchHistory()
    {
        if (SelectedBranch is not { } row) return;

        HistoryStitchPreview preview;
        try
        {
            preview = await _feedback.RunAsync(
                "Preview stitch",
                () => _git.PreviewHistoryStitchAsync(row.Name),
                p => p.CanExecute ? $"{p.UniqueSourceCommitCount} commits" : "blocked");
        }
        catch (Exception ex)
        {
            _windowService.Warning($"Could not preview history stitch:\n{ex.Message}", "Stitch history failed");
            return;
        }

        if (!_dialogs.ConfirmHistoryStitch(preview))
            return;

        try
        {
            var beforeSha = await _git.GetHeadShaAsync();
            await _snapshots.CaptureAsync(_git, $"Before history stitch from {row.Name}");

            var result = await _feedback.RunAsync(
                "Stitch history",
                () => _git.StitchHistoryAsync(row.Name),
                r => ShortSha(r.MergeCommitSha));

            await _opsLog.RecordAsync(new OperationRecord(
                Id:             Guid.NewGuid().ToString(),
                Timestamp:      DateTimeOffset.Now,
                Kind:           OperationKind.Merge,
                Description:    $"Stitch history from {result.SourceRef}",
                BranchName:     result.TargetBranch,
                BeforeSha:      beforeSha,
                AfterSha:       result.MergeCommitSha,
                ReflogSelector: null,
                Status:         OperationStatus.Active));

            await AfterChangeAsync();

            _windowService.Info(
                $"History stitched from '{result.SourceRef}'.\n\n" +
                $"Merge commit: {ShortSha(result.MergeCommitSha)}\n" +
                $"Backup branch: {result.BackupBranch}\n\n" +
                "Current files were left unchanged.\n\n" +
                $"Before push, Gitster undo/reset or the backup branch is safe. After push, use git revert -m 1 {ShortSha(result.MergeCommitSha)}.",
                "History stitched");
        }
        catch (Exception ex)
        {
            _windowService.Error($"History stitch failed:\n{ex.Message}", "Gitster");
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task Rename()
    {
        if (SelectedBranch is not { } row) return;

        var value = _dialogs.PromptText(
            "Rename branch", $"New name for '{row.Name}':", row.IsRemote ? string.Empty : row.Name);
        if (value is null) return;

        var newName = value.Trim();
        if (string.IsNullOrEmpty(newName) || newName == row.Name) return;

        try
        {
            await _snapshots.CaptureAsync(_git, $"Rename branch {row.Name} → {newName}");
            await _feedback.RunAsync("Rename branch", () => _git.RenameBranchAsync(row.Name, newName));
            await AfterChangeAsync();
        }
        catch (Exception ex)
        {
            _windowService.Warning(ex.Message, "Rename failed");
        }
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task Delete()
    {
        if (SelectedBranch is not { } row) return;

        var warn = row.IsMerged
            ? $"Delete branch '{row.Name}'?"
            : $"Branch '{row.Name}' is NOT merged into the current branch.\n\n" +
              "Deleting it may permanently lose its commits. Delete anyway?";

        var confirm = _windowService.Ask(warn, "Delete branch",
            MessageButtons.YesNo, MessageIcon.Warning);
        if (confirm != MessageResult.Yes) return;

        try
        {
            await _snapshots.CaptureAsync(_git, $"Delete branch {row.Name}");
            await _feedback.RunAsync("Delete branch", () => _git.DeleteBranchAsync(row.Name, force: !row.IsMerged));
            await AfterChangeAsync();
        }
        catch (Exception ex)
        {
            _windowService.Warning(ex.Message, "Delete failed");
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task CreateFromHere()
    {
        if (SelectedBranch is not { } row) return;

        var value = _dialogs.PromptText(
            "Create branch", $"New branch name (starting from '{row.Name}'):", string.Empty);
        if (value is null) return;

        var name = value.Trim();
        if (string.IsNullOrEmpty(name)) return;

        try
        {
            await _snapshots.CaptureAsync(_git, $"Create branch {name}");
            await _feedback.RunAsync("Create branch", () => _git.CreateBranchAsync(name, row.Info.TipSha));
            await AfterChangeAsync();
        }
        catch (Exception ex)
        {
            _windowService.Warning(ex.Message, "Create failed");
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task Archive()
    {
        if (SelectedBranch is not { } row)
            return;

        await _archiveService.ArchiveRefAsync(
            row.Name,
            $"branch-{row.Name}",
            row.Info.TipSha);
    }

    private async Task AfterChangeAsync()
    {
        await LoadAsync();
        await _onChanged();
    }

    private static string ShortSha(string sha) => GitSha.Short(sha);

    private static string DescribeStrategy(BranchMergeStrategy strategy) => strategy switch
    {
        BranchMergeStrategy.FastForwardOnly => "fast-forward only",
        BranchMergeStrategy.NoFastForward => "no fast-forward",
        _ => "default",
    };

    private static string DescribeMergeOutcome(BranchMergeResult result) => result.Outcome switch
    {
        BranchMergeOutcome.UpToDate => "Already up to date",
        BranchMergeOutcome.FastForward => $"Fast-forward to {ShortSha(result.HeadSha)}",
        _ => $"Merge commit {ShortSha(result.HeadSha)}",
    };
}
