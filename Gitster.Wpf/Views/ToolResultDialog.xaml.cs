using System.Windows;
using System.Windows.Media;

namespace Gitster.Views;

public partial class ToolResultDialog : Window
{
    public ToolResultDialog(string toolName, int exitCode, string output)
    {
        InitializeComponent();
        Title = $"{toolName} — output";

        var success = exitCode == 0;
        StatusIcon.Text = success ? "✓" : "!";
        var resources = Application.Current?.Resources;
        StatusIcon.Foreground = success
            ? (Brush)(resources?["AccentSuccess"] ?? Brushes.ForestGreen)
            : (Brush)(resources?["AccentDanger"] ?? Brushes.IndianRed);
        StatusText.Text = success ? "Finished successfully" : $"Exited with code {exitCode}";
        OutputBox.Text = string.IsNullOrWhiteSpace(output) ? "(no output)" : output;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
