using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using Gitster.Models;
using Gitster.ViewModels;

namespace Gitster.Controls;

/// <summary>
/// Reusable master-detail diff renderer. State lives in <see cref="DiffViewModel"/>;
/// this code-behind keeps the WPF dependency-property and tree-selection plumbing.
/// </summary>
public partial class DiffView : UserControl
{
    private bool _syncingSelectedFile;

    public DiffView()
    {
        ViewModel = new DiffViewModel();
        InitializeComponent();
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DiffViewModel.SelectedFile))
                SyncSelectedFileFromViewModel();
        };
    }

    public DiffViewModel ViewModel { get; }

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
            new PropertyMetadata(null, OnSelectedFileChanged));

    public DiffFileEntry? SelectedFile
    {
        get => (DiffFileEntry?)GetValue(SelectedFileProperty);
        set => SetValue(SelectedFileProperty, value);
    }

    public static readonly DependencyProperty RemoveChangeFromCommitCommandProperty =
        DependencyProperty.Register(nameof(RemoveChangeFromCommitCommand), typeof(ICommand), typeof(DiffView),
            new PropertyMetadata(null));

    public ICommand? RemoveChangeFromCommitCommand
    {
        get => (ICommand?)GetValue(RemoveChangeFromCommitCommandProperty);
        set => SetValue(RemoveChangeFromCommitCommandProperty, value);
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
        ((DiffView)d).ViewModel.Header = (string?)e.NewValue ?? string.Empty;
    }

    private static void OnFilesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((DiffView)d).ViewModel.SetFiles((IEnumerable?)e.NewValue);
    }

    private static void OnSelectedFileChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (DiffView)d;
        if (view._syncingSelectedFile)
            return;

        view.ViewModel.SelectedFile = (DiffFileEntry?)e.NewValue;
    }

    private void SyncSelectedFileFromViewModel()
    {
        if (Equals(SelectedFile, ViewModel.SelectedFile))
            return;

        _syncingSelectedFile = true;
        try
        {
            SetCurrentValue(SelectedFileProperty, ViewModel.SelectedFile);
        }
        finally
        {
            _syncingSelectedFile = false;
        }
    }

    private void FileTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        ViewModel.SelectFromTreeItem(e.NewValue as DiffTreeNode);
    }

    private void TreeViewItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is TreeViewItem item)
        {
            item.Focus();
            item.IsSelected = true;
        }
    }

    private void FileTreeView_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (!ViewModel.CanOpenFileContextMenu(RemoveChangeFromCommitCommand))
            e.Handled = true;
    }
}
