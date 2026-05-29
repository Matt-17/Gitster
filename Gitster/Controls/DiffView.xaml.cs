using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace Gitster.Controls;

/// <summary>
/// One reusable diff renderer (plan B7): a header summary over a collapsible per-file list,
/// each file showing its unified diff with add/remove colouring. Used by the Commits mode and
/// the Search results.
/// </summary>
public partial class DiffView : UserControl
{
    public DiffView()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(DiffView),
            new PropertyMetadata("no commit selected"));

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public static readonly DependencyProperty FilesProperty =
        DependencyProperty.Register(nameof(Files), typeof(IEnumerable), typeof(DiffView),
            new PropertyMetadata(null));

    public IEnumerable? Files
    {
        get => (IEnumerable?)GetValue(FilesProperty);
        set => SetValue(FilesProperty, value);
    }
}
