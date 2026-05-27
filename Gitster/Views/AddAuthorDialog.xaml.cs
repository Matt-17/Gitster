using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;

using Gitster.Models;

namespace Gitster.Views;

public partial class AddAuthorDialog : Window
{
    public AuthorEntry? Result { get; private set; }

    public AddAuthorDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => NameBox.Focus();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        var name  = NameBox.Text.Trim();
        var email = EmailBox.Text.Trim();

        if (string.IsNullOrEmpty(name))
        {
            ShowError("Name is required.");
            return;
        }

        if (!string.IsNullOrEmpty(email) &&
            !Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
        {
            ShowError("Enter a valid email address.");
            return;
        }

        if (GlobalCheck.IsChecked == true)
        {
            try
            {
                Process.Start(new ProcessStartInfo("git",
                    $"config --global user.name \"{name}\"")
                    { CreateNoWindow = true })?.WaitForExit();
                if (!string.IsNullOrEmpty(email))
                    Process.Start(new ProcessStartInfo("git",
                        $"config --global user.email \"{email}\"")
                        { CreateNoWindow = true })?.WaitForExit();
            }
            catch { /* best-effort */ }
        }

        Result = new AuthorEntry(name, email);
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = System.Windows.Visibility.Visible;
    }
}
