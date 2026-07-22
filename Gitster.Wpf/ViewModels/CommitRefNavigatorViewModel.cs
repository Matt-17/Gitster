using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Gitster.Services;
using Gitster.Core;
using Gitster.Core.Git;
using Gitster.Core.History;

namespace Gitster.ViewModels;

public sealed class CommitRefNode : ObservableObject
{
    private bool _isExpanded = true;
    private bool _isViewed;

    public CommitRefNode(string name, RefCatalogItem? item, string folderPath = "")
    {
        Name = name;
        Item = item;
        FolderPath = folderPath;
    }

    public string Name { get; }
    public string FolderPath { get; }
    public RefCatalogItem? Item { get; }
    public ObservableCollection<CommitRefNode> Children { get; } = [];

    public bool IsRef => Item is not null;
    public bool IsFolder => Item is null;
    public bool IsCurrent => Item?.IsCurrent ?? false;
    public bool IsLocalBranch => Item?.Kind == RefCatalogKind.LocalBranch;
    public bool IsRemoteBranch => Item?.Kind == RefCatalogKind.RemoteBranch;
    public bool IsTag => Item?.Kind == RefCatalogKind.Tag;
    public bool IsOtherRef => Item is { Kind: not RefCatalogKind.LocalBranch and not RefCatalogKind.RemoteBranch and not RefCatalogKind.Tag };
    public bool MissingUpstream => Item?.Kind == RefCatalogKind.LocalBranch && !Item.HasUpstream;
    public string? CanonicalName => Item?.CanonicalName;
    public string DisplayPath => Item?.DisplayName ?? FolderPath;
    public string PinText => IsPinned ? "P" : string.Empty;
    public string MissingUpstreamText => MissingUpstream ? "no upstream" : string.Empty;
    public string AheadBehindText =>
        Item is { Ahead: > 0, Behind: > 0 } item ? $"↑{item.Ahead} ↓{item.Behind}"
        : Item is { Ahead: > 0 } ahead ? $"↑{ahead.Ahead}"
        : Item is { Behind: > 0 } behind ? $"↓{behind.Behind}"
        : string.Empty;

    public bool IsPinned { get; init; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    /// <summary>True when this ref's history is the one currently shown in the commit list.</summary>
    public bool IsViewed
    {
        get => _isViewed;
        set => SetProperty(ref _isViewed, value);
    }
}

public partial class CommitRefNavigatorViewModel : BaseViewModel
{
    /// <summary>
    /// Local-branch count below which the "Branches" group (and its sub-folders) auto-expands.
    /// Above it we leave the tree collapsed so a large branch set stays manageable.
    /// </summary>
    private const int AutoExpandBranchLimit = 30;

    private readonly IGitBackend _git;
    private readonly UiPreferencesService _ui;
    private readonly BranchFavoritesService _favorites;
    private List<RefCatalogItem> _allRefs = [];

    // Remembers each folder node's expanded/collapsed state (keyed by its path) so that a
    // refresh — e.g. while committing — rebuilds the tree without losing what the user opened.
    private readonly Dictionary<string, bool> _expansionState = new(StringComparer.Ordinal);

    // A filtered tree is force-expanded, so its state must not be snapshotted back as user intent.
    private bool _treeFiltered;

    // What the commit list currently shows, so the matching node stays highlighted
    // across tree rebuilds (refresh, filter, favourites changes).
    private HistoryScope _viewedScope = HistoryScope.CurrentBranch;
    private string? _viewedCanonicalName;

    public CommitRefNavigatorViewModel(
        IGitBackend git,
        UiPreferencesService ui,
        BranchFavoritesService favorites)
    {
        _git = git;
        _ui = ui;
        _favorites = favorites;
        IsCollapsed = _ui.CommitRefPaneCollapsed;
        _favorites.Changed += Rebuild;
    }

    public ObservableCollection<CommitRefNode> RefTree { get; } = [];

    public Func<RefCatalogItem, Task>? SelectRefAsync { get; set; }

    /// <summary>Checks out a local branch — used by the double-click in the refs pane.</summary>
    public Func<string, Task>? CheckoutBranchAsync { get; set; }
    public Func<Task>? SelectCurrentBranchAsync { get; set; }
    public Func<Task>? SelectAllBranchesAsync { get; set; }

    [ObservableProperty]
    public partial string FilterText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial CommitRefNode? SelectedNode { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExpanded))]
    public partial bool IsCollapsed { get; set; }

    [ObservableProperty]
    public partial bool HasRefs { get; set; }

    public bool IsExpanded => !IsCollapsed;

    partial void OnIsCollapsedChanged(bool value) => _ui.CommitRefPaneCollapsed = value;

    partial void OnFilterTextChanged(string value) => Rebuild();

    partial void OnSelectedNodeChanged(CommitRefNode? value)
    {
        if (value?.Item is { } item)
            _ = SelectRefAsync?.Invoke(item);
    }

    public async Task LoadAsync()
    {
        try
        {
            _allRefs = (await _git.GetRefCatalogAsync()).ToList();
        }
        catch
        {
            _allRefs = [];
        }

        Rebuild();
    }

    public void Clear()
    {
        _allRefs = [];
        RefTree.Clear();
        _expansionState.Clear();
        _treeFiltered = false;
        SelectedNode = null;
        HasRefs = false;
    }

    /// <summary>Marks the ref whose history the commit list shows (the "viewed" branch).</summary>
    public void SetViewedTarget(HistoryScope scope, string? canonicalName)
    {
        _viewedScope = scope;
        _viewedCanonicalName = canonicalName;
        ApplyViewedMarker();
    }

    private void ApplyViewedMarker()
    {
        foreach (var root in RefTree)
            ApplyViewedMarker(root);
    }

    private void ApplyViewedMarker(CommitRefNode node)
    {
        node.IsViewed = node.Item is { } item && _viewedScope switch
        {
            HistoryScope.Ref => string.Equals(item.CanonicalName, _viewedCanonicalName, StringComparison.Ordinal),
            HistoryScope.CurrentBranch => item.IsCurrent,
            _ => false,
        };

        foreach (var child in node.Children)
            ApplyViewedMarker(child);
    }

    [RelayCommand]
    private void ToggleCollapsed() => IsCollapsed = !IsCollapsed;

    [RelayCommand]
    private async Task ShowCurrentBranch()
    {
        // The commit list moves away from the tree selection, so drop it — otherwise
        // re-clicking the same branch later would be a no-op property change.
        SelectedNode = null;
        if (SelectCurrentBranchAsync is not null)
            await SelectCurrentBranchAsync();
    }

    [RelayCommand]
    private async Task ShowAllBranches()
    {
        SelectedNode = null;
        if (SelectAllBranchesAsync is not null)
            await SelectAllBranchesAsync();
    }

    [RelayCommand]
    private void ClearFilter() => FilterText = string.Empty;

    [RelayCommand]
    private void SelectNode(CommitRefNode? node)
    {
        if (node?.Item is null)
            return;

        // Re-clicking the already-selected node must still re-target the history view:
        // the commit list may have moved elsewhere in the meantime (scope buttons,
        // "show local history" link, checkout) without touching the tree selection.
        if (ReferenceEquals(SelectedNode, node))
        {
            _ = SelectRefAsync?.Invoke(node.Item);
            return;
        }

        SelectedNode = node;
    }

    /// <summary>Double-click on a local branch: check it out. Folders, tags and remotes do nothing.</summary>
    [RelayCommand]
    private async Task CheckoutNode(CommitRefNode? node)
    {
        if (node?.Item is not { Kind: RefCatalogKind.LocalBranch, IsCurrent: false } item
            || CheckoutBranchAsync is null)
        {
            return;
        }

        SelectedNode = node;
        await CheckoutBranchAsync(item.DisplayName);
    }

    private void Rebuild()
    {
        // Preserve any expander state the user changed before we throw the old tree away.
        // Skip a force-expanded filtered tree so it can't overwrite the user's real intent.
        if (!_treeFiltered)
            SnapshotExpansion();

        var query = FilterText.Trim();
        var hasFilter = !string.IsNullOrWhiteSpace(query);
        var refs = _allRefs.AsEnumerable();
        if (hasFilter)
            refs = refs.Where(r => r.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || r.CanonicalName.Contains(query, StringComparison.OrdinalIgnoreCase));

        var localBranchCount = _allRefs.Count(r => r.Kind == RefCatalogKind.LocalBranch);
        // A filter should surface every match, so expand everything while one is active.
        var expandBranches = hasFilter || localBranchCount < AutoExpandBranchLimit;

        var roots = new List<CommitRefNode>
        {
            BuildGroup("Branches", refs.Where(r => r.Kind == RefCatalogKind.LocalBranch), defaultExpanded: expandBranches),
            BuildGroup("Remotes", refs.Where(r => r.Kind == RefCatalogKind.RemoteBranch), defaultExpanded: hasFilter),
            BuildGroup("Tags", refs.Where(r => r.Kind == RefCatalogKind.Tag), defaultExpanded: hasFilter),
            BuildGroup("Other refs", refs.Where(r => r.Kind is RefCatalogKind.Stash or RefCatalogKind.Note or RefCatalogKind.Replace), defaultExpanded: hasFilter),
        }.Where(n => n.Children.Count > 0).ToList();

        RefTree.Clear();
        foreach (var root in roots)
            RefTree.Add(root);

        _treeFiltered = hasFilter;
        HasRefs = RefTree.Count > 0;
        ApplyViewedMarker();
    }

    private void SnapshotExpansion()
    {
        foreach (var root in RefTree)
            SnapshotExpansion(root);
    }

    private void SnapshotExpansion(CommitRefNode node)
    {
        if (node.IsFolder)
        {
            _expansionState[node.FolderPath] = node.IsExpanded;
            foreach (var child in node.Children)
                SnapshotExpansion(child);
        }
    }

    private CommitRefNode BuildGroup(string name, IEnumerable<RefCatalogItem> refs, bool defaultExpanded)
    {
        var root = new CommitRefNode(name, null, name) { IsExpanded = ResolveExpanded(name, defaultExpanded) };
        var folders = new Dictionary<string, CommitRefNode>(StringComparer.Ordinal);

        foreach (var item in refs.OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var display = item.DisplayName;
            var segments = display.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
                segments = [display];

            IList<CommitRefNode> level = root.Children;
            var path = name;

            for (var i = 0; i < segments.Length; i++)
            {
                var isLeaf = i == segments.Length - 1;
                path = $"{path}/{segments[i]}";

                if (isLeaf)
                {
                    level.Add(new CommitRefNode(
                        segments[i],
                        item,
                        path)
                    {
                        IsPinned = item.Kind == RefCatalogKind.LocalBranch
                            && ((_favorites.IsGlobalFavorite(item.DisplayName))
                                || _favorites.IsPinned(_git.RepositoryPath, item.DisplayName)),
                    });
                }
                else if (folders.TryGetValue(path, out var folder))
                {
                    level = folder.Children;
                }
                else
                {
                    folder = new CommitRefNode(segments[i], null, path)
                    {
                        IsExpanded = ResolveExpanded(path, defaultExpanded),
                    };
                    folders[path] = folder;
                    level.Add(folder);
                    level = folder.Children;
                }
            }
        }

        SortLevel(root.Children);
        return root;
    }

    // A folder the user has already touched keeps its remembered state; a brand-new one
    // follows the group default (auto-expanded local branches / filtered results).
    private bool ResolveExpanded(string path, bool defaultExpanded) =>
        _expansionState.TryGetValue(path, out var remembered) ? remembered : defaultExpanded;

    private static void SortLevel(ObservableCollection<CommitRefNode> level)
    {
        var sorted = level
            .OrderByDescending(n => n.IsFolder)
            .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        level.Clear();
        foreach (var node in sorted)
        {
            level.Add(node);
            if (node.IsFolder)
                SortLevel(node.Children);
        }
    }
}
