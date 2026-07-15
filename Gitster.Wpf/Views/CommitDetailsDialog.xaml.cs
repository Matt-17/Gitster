using System.Windows;

using Gitster.Core.Git;

namespace Gitster.Views;

public partial class CommitDetailsDialog : Window
{
    public CommitDetailsDialog(CommitDetails details)
    {
        InitializeComponent();
        ShaBox.Text = details.Sha;
        SummaryBox.Text = details.Message.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? details.Message;
        AuthorBox.Text = string.IsNullOrWhiteSpace(details.AuthorEmail)
            ? details.AuthorName
            : $"{details.AuthorName} <{details.AuthorEmail}>";
        CommitterBox.Text = string.IsNullOrWhiteSpace(details.CommitterEmail)
            ? details.CommitterName
            : $"{details.CommitterName} <{details.CommitterEmail}>";
        DateBox.Text = details.Date.ToString("yyyy-MM-dd HH:mm:ss");
        CommitterDateBox.Text = details.CommitterDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? "—";
        MessageBox.Text = details.Message;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
