using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Gitster.Core.Models;

namespace Gitster.ViewModels;

public sealed partial class DiffViewModel : ObservableObject
{
    public const int InitialVisibleLineLimit = 400;

    private int _fileCount;
    private bool _showAllSelectedFileLines;
    private bool _updatingTreeSelection;
    private DiffTreeNode? _selectedNode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmptyMessage))]
    [NotifyPropertyChangedFor(nameof(IsLoading))]
    public partial string Header { get; set; } = "no commit selected";

    [ObservableProperty]
    public partial DiffFileEntry? SelectedFile { get; set; }

    partial void OnSelectedFileChanged(DiffFileEntry? value)
    {
        _showAllSelectedFileLines = false;
        OnSelectedFileDisplayChanged();
    }

    public ObservableCollection<DiffTreeNode> FileTree { get; } = [];

    public string FileCountText => _fileCount == 1 ? "1 file" : $"{_fileCount} files";

    public string EmptyMessage =>
        string.Equals(Header, "loading...", StringComparison.OrdinalIgnoreCase)
            ? "Loading diff..."
            : _fileCount == 0 ? "No diff to display" : "Select a file";

    public bool IsLoading =>
        string.Equals(Header, "loading...", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<DiffLine> VisibleSelectedLines
    {
        get
        {
            var lines = SelectedFile?.Lines;
            if (lines is null || _showAllSelectedFileLines)
                return lines ?? Array.Empty<DiffLine>();

            return lines.Count <= InitialVisibleLineLimit
                ? lines
                : lines.Take(InitialVisibleLineLimit).ToArray();
        }
    }

    public bool IsSelectedDiffCapped =>
        SelectedFile?.Lines?.Count > InitialVisibleLineLimit
        && !_showAllSelectedFileLines;

    public string HiddenLineText
    {
        get
        {
            var hidden = (SelectedFile?.Lines?.Count ?? 0) - InitialVisibleLineLimit;
            return hidden > 0 ? $"{hidden:N0} more lines" : string.Empty;
        }
    }

    public void SetFiles(IEnumerable? files)
    {
        var previousPath = SelectedFile?.Path;
        var entries = files?.OfType<DiffFileEntry>().ToList() ?? [];

        _selectedNode = null;
        FileTree.Clear();
        foreach (var file in entries)
            AddFileNode(file);

        UpdateFolderCounts(FileTree);
        _fileCount = entries.Count;

        var selectedNode = previousPath is null ? null : FindFileNode(FileTree, previousPath);
        selectedNode ??= FindFirstFileNode(FileTree);
        SelectNode(selectedNode);

        OnPropertyChanged(nameof(FileCountText));
        OnPropertyChanged(nameof(EmptyMessage));
    }

    public void SelectNode(DiffTreeNode? selectedNode) => SelectNodeCore(selectedNode, forceApplyTreeSelection: false);

    private void SelectNodeCore(DiffTreeNode? selectedNode, bool forceApplyTreeSelection)
    {
        if (selectedNode?.File is null)
            selectedNode = null;

        if (!forceApplyTreeSelection
            && ReferenceEquals(_selectedNode, selectedNode)
            && Equals(SelectedFile, selectedNode?.File))
        {
            return;
        }

        _selectedNode = selectedNode;
        ApplyTreeSelection(selectedNode);
        SelectedFile = selectedNode?.File;
    }

    public void SelectFromTreeItem(DiffTreeNode? node)
    {
        if (_updatingTreeSelection)
            return;

        SelectNodeCore(node?.File is null ? null : node, forceApplyTreeSelection: node?.File is null);
    }

    public bool CanOpenFileContextMenu(ICommand? removeChangeCommand) =>
        SelectedFile is not null
        && removeChangeCommand is not null
        && removeChangeCommand.CanExecute(SelectedFile);

    [RelayCommand]
    private void ShowAllLines()
    {
        _showAllSelectedFileLines = true;
        OnSelectedFileDisplayChanged();
    }

    private void AddFileNode(DiffFileEntry file)
    {
        var parts = SplitPath(file.Path);
        var nodes = FileTree;
        var folderPath = string.Empty;

        for (var i = 0; i < parts.Count - 1; i++)
        {
            var part = parts[i];
            folderPath = string.IsNullOrEmpty(folderPath) ? part : $"{folderPath}/{part}";
            var folder = GetOrAddFolder(nodes, part, folderPath, i);
            nodes = folder.Children;
        }

        nodes.Add(DiffTreeNode.ForFile(parts[^1], file, parts.Count - 1));
    }

    private static List<string> SplitPath(string path)
    {
        var parts = path
            .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        return parts.Count == 0 ? [path] : parts;
    }

    private static DiffTreeNode GetOrAddFolder(
        ObservableCollection<DiffTreeNode> nodes,
        string name,
        string fullPath,
        int depth)
    {
        foreach (var node in nodes)
        {
            if (node.IsFolder && string.Equals(node.Name, name, StringComparison.OrdinalIgnoreCase))
                return node;
        }

        var folder = DiffTreeNode.ForFolder(name, fullPath, depth);
        nodes.Add(folder);
        return folder;
    }

    private static int UpdateFolderCounts(IEnumerable<DiffTreeNode> nodes)
    {
        var total = 0;
        foreach (var node in nodes)
        {
            if (node.IsFile)
            {
                total++;
                continue;
            }

            node.FileCount = UpdateFolderCounts(node.Children);
            total += node.FileCount;
        }

        return total;
    }

    private static DiffTreeNode? FindFirstFileNode(IEnumerable<DiffTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.IsFile)
                return node;

            var childMatch = FindFirstFileNode(node.Children);
            if (childMatch is not null)
                return childMatch;
        }

        return null;
    }

    private static DiffTreeNode? FindFileNode(IEnumerable<DiffTreeNode> nodes, string path)
    {
        foreach (var node in nodes)
        {
            if (node.File is not null && string.Equals(node.File.Path, path, StringComparison.OrdinalIgnoreCase))
                return node;

            var childMatch = FindFileNode(node.Children, path);
            if (childMatch is not null)
                return childMatch;
        }

        return null;
    }

    private void OnSelectedFileDisplayChanged()
    {
        OnPropertyChanged(nameof(VisibleSelectedLines));
        OnPropertyChanged(nameof(IsSelectedDiffCapped));
        OnPropertyChanged(nameof(HiddenLineText));
        OnPropertyChanged(nameof(EmptyMessage));
    }

    private void ApplyTreeSelection(DiffTreeNode? selectedNode)
    {
        _updatingTreeSelection = true;
        try
        {
            ApplyTreeSelection(FileTree, selectedNode);
        }
        finally
        {
            _updatingTreeSelection = false;
        }
    }

    private static void ApplyTreeSelection(IEnumerable<DiffTreeNode> nodes, DiffTreeNode? selectedNode)
    {
        foreach (var node in nodes)
        {
            node.IsSelected = ReferenceEquals(node, selectedNode);
            ApplyTreeSelection(node.Children, selectedNode);
        }
    }
}

public sealed class DiffTreeNode : INotifyPropertyChanged
{
    private bool _isExpanded = true;
    private bool _isSelected;
    private int _fileCount;

    private DiffTreeNode(string name, string fullPath, DiffFileEntry? file, int depth)
    {
        Name = name;
        FullPath = fullPath;
        File = file;
        Depth = depth;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; }

    public string FullPath { get; }

    public DiffFileEntry? File { get; }

    public int Depth { get; }

    public ObservableCollection<DiffTreeNode> Children { get; } = [];

    public bool IsFile => File is not null;

    public bool IsFolder => File is null;

    public string Status => File?.Status ?? string.Empty;

    public string StatusText => Status switch
    {
        "A" => "Added",
        "D" => "Deleted",
        "R" => "Renamed",
        "M" => "Modified",
        _ => Status,
    };

    public string CountsText => File is null ? string.Empty : $"+{File.Added} -{File.Deleted}";

    public string TooltipText => File is null
        ? $"{FullPath}{Environment.NewLine}{FileCountText}"
        : $"{FullPath}{Environment.NewLine}Status: {StatusText}{Environment.NewLine}Lines: {CountsText}";

    public int LeadingBadgeOffset => -(16 + 14 * Depth);

    public int FileCount
    {
        get => _fileCount;
        set
        {
            if (_fileCount == value)
                return;

            _fileCount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FileCountText));
            OnPropertyChanged(nameof(TooltipText));
        }
    }

    public string FileCountText => FileCount == 1 ? "1 file" : $"{FileCount} files";

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public static DiffTreeNode ForFolder(string name, string fullPath, int depth) => new(name, fullPath, null, depth);

    public static DiffTreeNode ForFile(string name, DiffFileEntry file, int depth) => new(name, file.Path, file, depth);

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
