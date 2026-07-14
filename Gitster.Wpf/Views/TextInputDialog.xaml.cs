using System.Windows;

namespace Gitster.Views;

public partial class TextInputDialog : Window
{
    public TextInputDialog()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    public string Prompt
    {
        get => PromptLabel.Text;
        set => PromptLabel.Text = value;
    }

    public string Value
    {
        get => ValueBox.Text;
        set => ValueBox.Text = value;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
