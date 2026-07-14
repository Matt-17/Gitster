using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Gitster.Controls;

public partial class CommitContextCard : UserControl
{
    public CommitContextCard()
    {
        InitializeComponent();
    }

    private void CopySha_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string sha && !string.IsNullOrEmpty(sha))
            CopyWithFeedback(btn, sha, "Copy full SHA");
    }

    private void CopyMessage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string msg && !string.IsNullOrEmpty(msg))
            CopyWithFeedback(btn, msg, "Copy full message");
    }

    private static void CopyWithFeedback(Button btn, string text, string originalTip)
    {
        try { Clipboard.SetText(text); } catch { return; }
        btn.ToolTip = "Copied!";
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        timer.Tick += (_, _) => { btn.ToolTip = originalTip; timer.Stop(); };
        timer.Start();
    }
}
