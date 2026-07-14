using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

using Gitster.Models;

namespace Gitster.Views;

public enum SnapshotRestoreChoice
{
    None,
    CurrentBranch,
    AllRefs,
}

public partial class SnapshotBrowserDialog : Window, INotifyPropertyChanged
{
    private RepositorySnapshot? _selectedSnapshot;

    public SnapshotBrowserDialog(IReadOnlyList<RepositorySnapshot> snapshots)
    {
        InitializeComponent();
        Snapshots = new ObservableCollection<RepositorySnapshot>(snapshots);
        SelectedSnapshot = Snapshots.FirstOrDefault();
        DataContext = this;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<RepositorySnapshot> Snapshots { get; }

    public RepositorySnapshot? SelectedSnapshot
    {
        get => _selectedSnapshot;
        set
        {
            if (_selectedSnapshot == value)
                return;

            _selectedSnapshot = value;
            OnPropertyChanged();
        }
    }

    public SnapshotRestoreChoice Choice { get; private set; }

    private void RestoreBranch_Click(object sender, RoutedEventArgs e)
    {
        Choice = SnapshotRestoreChoice.CurrentBranch;
        DialogResult = true;
    }

    private void RestoreAll_Click(object sender, RoutedEventArgs e)
    {
        Choice = SnapshotRestoreChoice.AllRefs;
        DialogResult = true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
