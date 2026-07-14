using System.Windows;
using System.Windows.Controls;

using Gitster.Services.OperationsLog;

namespace Gitster.Views;

public partial class OperationsLogWindow : Window
{
    private readonly OperationsLogService _log;
    private string _statusFilter = "All";
    private string _kindFilter = "All";

    public OperationsLogWindow(OperationsLogService log, string repoName)
    {
        _log = log;
        InitializeComponent();
        base.Title = string.IsNullOrWhiteSpace(repoName)
            ? "Operations log – Gitster"
            : $"Operations log – {repoName}";

        _log.Changed += (_, _) => Dispatcher.BeginInvoke(ApplyFilter);
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var records = _log.Records.AsEnumerable();

        if (_statusFilter != "All" && Enum.TryParse<OperationStatus>(_statusFilter, out var status))
            records = records.Where(r => r.Status == status);

        if (_kindFilter != "All" && Enum.TryParse<OperationKind>(_kindFilter, out var kind))
            records = records.Where(r => r.Kind == kind);

        RecordList?.ItemsSource = records.ToList();
    }

    private void StatusFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _statusFilter = (StatusFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All";
        ApplyFilter();
    }

    private void KindFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _kindFilter = (KindFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All";
        // Normalise display name back to enum name
        _kindFilter = _kindFilter == "Cherry-pick" ? "CherryPick" : _kindFilter;
        ApplyFilter();
    }
}
