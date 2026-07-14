using System.Windows.Controls;

using Gitster.Core.OperationsLog;
using Gitster.ViewModels;

namespace Gitster.Views.Modes;

public partial class OperationsLogModeView : UserControl
{
    private OperationsLogService? _log;
    private string _statusFilter = "All";
    private string _kindFilter = "All";

    public OperationsLogModeView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Reconnect();
        Loaded += (_, _) => Reconnect();
    }

    private void Reconnect()
    {
        if (_log != null)
            _log.Changed -= OnLogChanged;

        _log = null;

        if (DataContext is MainWindowViewModel vm)
        {
            _log = vm.OpsLogService;
            _log.Changed += OnLogChanged;
        }

        ApplyFilter();
    }

    private void OnLogChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(ApplyFilter);

    private void ApplyFilter()
    {
        if (_log == null)
        {
            RecordList?.ItemsSource = null;
            return;
        }

        var records = _log.Records.AsEnumerable();

        if (_statusFilter != "All" && Enum.TryParse<OperationStatus>(_statusFilter, out var status))
            records = records.Where(r => r.Status == status);

        if (_kindFilter != "All" && Enum.TryParse<OperationKind>(_kindFilter, out var kind))
            records = records.Where(r => r.Kind == kind);

        RecordList.ItemsSource = records.ToList();
    }

    private void StatusFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _statusFilter = (StatusFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All";
        ApplyFilter();
    }

    private void KindFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = (KindFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All";
        _kindFilter = selected == "Cherry-pick" ? "CherryPick" : selected;
        ApplyFilter();
    }
}
