using System.Windows;

namespace Gitster.Views;

public partial class RewordDialog : Window
{
    public string NewMessage => MessageBox.Text;

    public RewordDialog(string currentMessage)
    {
        InitializeComponent();
        MessageBox.Text = currentMessage;
        Loaded += (_, _) =>
        {
            MessageBox.Focus();
            MessageBox.SelectAll();
        };
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(MessageBox.Text))
        {
            GitsterDialog.Show(
                this,
                "A commit message cannot be empty.",
                "Gitster", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) =>
        DialogResult = false;
}
