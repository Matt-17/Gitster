using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

using Gitster.Models;

namespace Gitster.Controls;

/// <summary>
/// Reusable master-detail diff renderer: file tree on the left, selected unified diff on the right.
/// Used by the Commits mode and the Search results.
/// </summary>
public partial class DiffView : UserControl, INotifyPropertyChanged
{
    private int _fileCount;
    private bool _syncingSelection;

    public DiffView()
    {
        InitializeComponent();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<DiffTreeNode> FileTree { get; } = [];

    public string FileCountText => _fileCount == 1 ? "1 file" : $"{_fileCount} files";

    public string EmptyMessage =>
        string.Equals(Header, "loading...", StringComparison.OrdinalIgnoreCase)
            ? "Loading diff..."
            : _fileCount == 0 ? "No diff to display" : "Select a file";

    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(DiffView),
            new PropertyMetadata("no commit selected", OnHeaderChanged));

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public static readonly DependencyProperty FilesProperty =
        DependencyProperty.Register(nameof(Files), typeof(IEnumerable), typeof(DiffView),
            new PropertyMetadata(null, OnFilesChanged));

    public IEnumerable? Files
    {
        get => (IEnumerable?)GetValue(FilesProperty);
        set => SetValue(FilesProperty, value);
    }

    public static readonly DependencyProperty SelectedFileProperty =
        DependencyProperty.Register(nameof(SelectedFile), typeof(DiffFileEntry), typeof(DiffView),
            new PropertyMetadata(null));

    public DiffFileEntry? SelectedFile
    {
        get => (DiffFileEntry?)GetValue(SelectedFileProperty);
        set => SetValue(SelectedFileProperty, value);
    }

    public static readonly DependencyProperty FileTreeSplitterKeyProperty =
        DependencyProperty.Register(nameof(FileTreeSplitterKey), typeof(string), typeof(DiffView),
            new PropertyMetadata(string.Empty));

    public string FileTreeSplitterKey
    {
        get => (string)GetValue(FileTreeSplitterKeyProperty);
        set => SetValue(FileTreeSplitterKeyProperty, value);
    }

    private static void OnHeaderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((DiffView)d).OnPropertyChanged(nameof(EmptyMessage));
    }

    private static void OnFilesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((DiffView)d).RebuildFileTree();
    }

    private void FileTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_syncingSelection)
            return;

        if (e.NewValue is DiffTreeNode { File: { } file })
            SelectedFile = file;
    }

    private void RebuildFileTree()
    {
        var previousPath = SelectedFile?.Path;
        var files = Files?.OfType<DiffFileEntry>().ToList() ?? [];

        _syncingSelection = true;
        try
        {
            FileTree.Clear();
            foreach (var file in files)
                AddFileNode(file);

            UpdateFolderCounts(FileTree);
            _fileCount = files.Count;

            var selectedNode = previousPath is null ? null : FindFileNode(FileTree, previousPath);
            selectedNode ??= FindFirstFileNode(FileTree);
            SelectNode(selectedNode);
        }
        finally
        {
            _syncingSelection = false;
        }

        OnPropertyChanged(nameof(FileCountText));
        OnPropertyChanged(nameof(EmptyMessage));
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

    private void SelectNode(DiffTreeNode? selectedNode)
    {
        ClearSelection(FileTree);

        if (selectedNode is null)
        {
            SelectedFile = null;
            return;
        }

        selectedNode.IsSelected = true;
        SelectedFile = selectedNode.File;
    }

    private static void ClearSelection(IEnumerable<DiffTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            node.IsSelected = false;
            ClearSelection(node.Children);
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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
        _   => Status,
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
