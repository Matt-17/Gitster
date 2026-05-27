using System.Windows;

using Gitster.Services.OperationsLog;

namespace Gitster.Views;

public partial class UndoConfirmationDialog : Window
{
    public UndoConfirmationDialog(UndoPlan.Ready plan)
    {
        InitializeComponent();
        CommitList.ItemsSource = plan.WouldDiscard;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void UndoButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
