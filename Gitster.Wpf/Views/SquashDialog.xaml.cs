using System.Windows;

namespace Gitster.Views;

public partial class SquashDialog : Window
{
    public string CombinedMessage => MessageBox.Text;
    public DateTimeOffset? OverrideDate { get; private set; }

    public SquashDialog(string combinedMessage)
    {
        InitializeComponent();
        MessageBox.Text = combinedMessage;
        DatePicker.SelectedDate = DateTime.Now;
        OverrideDateCheck.IsChecked = false;
        DatePicker.IsEnabled = false;

        Loaded += (_, _) =>
        {
            MessageBox.Focus();
            MessageBox.CaretIndex = MessageBox.Text.Length;
        };
    }

    private void OverrideDateCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (DatePicker != null)
            DatePicker.IsEnabled = OverrideDateCheck.IsChecked == true;
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

        OverrideDate = OverrideDateCheck.IsChecked == true && DatePicker.SelectedDate.HasValue
            ? new DateTimeOffset(DatePicker.SelectedDate.Value)
            : null;

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) =>
        DialogResult = false;
}
