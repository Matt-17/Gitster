using System.Windows;

namespace Gitster.Views;

public partial class NewStashDialog : Window
{
    public string StashMessage    { get; private set; } = string.Empty;
    public bool   IncludeUntracked { get; private set; } = true;

    public NewStashDialog()
    {
        InitializeComponent();
        MessageTextBox.Focus();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        StashMessage     = MessageTextBox.Text.Trim();
        IncludeUntracked = UntrackedCheck.IsChecked == true;
        DialogResult     = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
