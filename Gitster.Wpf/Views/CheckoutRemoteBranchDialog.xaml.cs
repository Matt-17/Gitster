using System.Windows;
using System.Windows.Controls;

namespace Gitster.Views;

/// <summary>
/// Asks for the local name when a remote branch without a local counterpart is checked out.
/// </summary>
public partial class CheckoutRemoteBranchDialog : Window
{
    private readonly IReadOnlySet<string> _existingLocalNames;

    public CheckoutRemoteBranchDialog(string remoteBranchName, string suggestedLocalName, IReadOnlySet<string> existingLocalNames)
    {
        InitializeComponent();
        _existingLocalNames = existingLocalNames;
        PromptLabel.Text = $"Local branch name for {remoteBranchName}:";
        UpstreamCheck.Content = $"Track {remoteBranchName} as upstream";
        NameBox.Text = suggestedLocalName;
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    public string LocalName => NameBox.Text.Trim();
    public bool Checkout => CheckoutCheck.IsChecked == true;
    public bool SetUpstream => UpstreamCheck.IsChecked == true;

    private string? Validate()
    {
        var name = LocalName;
        if (name.Length == 0)
            return "Enter a branch name.";
        if (_existingLocalNames.Contains(name))
            return $"A local branch named '{name}' already exists.";
        if (name.Any(char.IsWhiteSpace))
            return "Branch names cannot contain spaces.";
        return null;
    }

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var error = Validate();
        ErrorLabel.Text = error ?? string.Empty;
        ErrorLabel.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
        OkButton.IsEnabled = error is null;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (Validate() is not null)
            return;

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
