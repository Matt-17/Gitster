using System.Windows;
using Gitster.Services.Features;

namespace Gitster.Views;

public partial class ConflictGuidanceDialog : Window
{
    private readonly ConflictGuidance _guidance;

    public ConflictGuidanceDialog(ConflictGuidance guidance)
    {
        InitializeComponent();
        _guidance = guidance;
        SelectedAction = ConflictGuidanceAction.Close;

        OperationText.Text = $"{guidance.OperationName} stopped on conflicts";
        StateText.Text = guidance.StateSummary;
        FilesList.ItemsSource = guidance.Files.Count > 0
            ? guidance.Files
            : new[] { "No conflicted files were reported." };
        RawMessageText.Text = guidance.RawMessage;
        OpenMergeToolButton.IsEnabled = guidance.CanOpenMergeTool;
    }

    public ConflictGuidanceAction SelectedAction { get; private set; }

    private void OnOpenMergeToolClicked(object sender, RoutedEventArgs e)
    {
        SelectedAction = ConflictGuidanceAction.OpenMergeTool;
        DialogResult = true;
    }

    private void OnRetryClicked(object sender, RoutedEventArgs e)
    {
        SelectedAction = ConflictGuidanceAction.Retry;
        DialogResult = true;
    }

    private void OnCopyListClicked(object sender, RoutedEventArgs e)
    {
        var text = _guidance.Files.Count > 0
            ? string.Join(Environment.NewLine, _guidance.Files)
            : _guidance.RawMessage;
        Clipboard.SetText(text);
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        SelectedAction = ConflictGuidanceAction.Close;
        DialogResult = false;
    }
}
