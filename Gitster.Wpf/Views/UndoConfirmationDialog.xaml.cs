using System.Windows;

using Gitster.Core.OperationsLog;

namespace Gitster.Views;

public partial class UndoConfirmationDialog : Window
{
    public bool ReplayOnTop { get; private set; }

    public UndoConfirmationDialog(UndoPlan.Ready plan)
    {
        InitializeComponent();
        CommitList.ItemsSource = plan.WouldDiscard;

        var n = plan.WouldDiscard.Count;
        var noun = n == 1 ? "commit" : "commits";
        DiscardButton.Content = $"Discard {n} {noun}";
        ReplayButton.Content  = $"Replay {n} {noun} on top";
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void DiscardButton_Click(object sender, RoutedEventArgs e)
    {
        ReplayOnTop = false;
        DialogResult = true;
        Close();
    }

    private void ReplayButton_Click(object sender, RoutedEventArgs e)
    {
        ReplayOnTop = true;
        DialogResult = true;
        Close();
    }
}
