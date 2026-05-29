using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Gitster.Models;
using Gitster.Services;
using Gitster.Services.Git;
using Gitster.Services.OperationsLog;
using Gitster.Views;

using LibGit2Sharp;

namespace Gitster.ViewModels;

/// <summary>UI wrapper around a <see cref="BranchListItem"/>.</summary>
public sealed class BranchRow
{
    public BranchRow(BranchListItem info) => Info = info;

    public BranchListItem Info { get; }

    public string  Name        => Info.Name;
    public bool    IsCurrent   => Info.IsCurrent;
    public bool    IsRemote    => Info.IsRemote;
    public bool    IsMerged    => Info.IsMerged;
    public string  TipMessage  => Info.TipMessage;
    public DateTimeOffset LastActivity => Info.LastActivity;
    public DateTime LastActivityLocal => Info.LastActivity.LocalDateTime;
    public string  ShortSha    => Info.TipSha.Length >= 7 ? Info.TipSha[..7] : Info.TipSha;
    public string? UpstreamName => Info.UpstreamName;

    public string  GroupLabel  => Info.IsRemote ? "Remote" : "Local";
    public int     GroupOrder  => Info.IsRemote ? 1 : 0;

    public bool    ShowAhead   => Info.Ahead  > 0;
    public bool    ShowBehind  => Info.Behind > 0;
    public string  AheadText   => $"↑{Info.Ahead}";
    public string  BehindText  => $"↓{Info.Behind}";

    public string  UpstreamDisplay => string.IsNullOrEmpty(Info.UpstreamName) ? "no upstream" : Info.UpstreamName!;
}

public partial class BranchesViewModel : BaseViewModel
{
    private readonly IGitBackend              _git;
    private readonly OperationFeedbackService _feedback;
    private readonly OperationsLogService     _opsLog;
    private readonly SnapshotService          _snapshots;
    private readonly Func<Task>               _onChanged;

    private List<BranchRow> _all = [];

    public ObservableCollection<BranchRow> Branches { get; } = [];

    /// <summary>Local branches only — backs the title-bar branch dropdown (plan A11).</summary>
    public ObservableCollection<BranchRow> LocalBranches { get; } = [];

    /// <summary>Branches grouped by Local/Remote for the section headers in the list.</summary>
    public ICollectionView GroupedBranches { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(CanCheckout))]
    [NotifyPropertyChangedFor(nameof(CanDelete))]
    [NotifyCanExecuteChangedFor(nameof(CheckoutCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    [NotifyCanExecuteChangedFor(nameof(RenameCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateFromHereCommand))]
    public partial BranchRow? SelectedBranch { get; set; }

    [ObservableProperty]
    public partial string FilterText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortByDateActive))]
    [NotifyPropertyChangedFor(nameof(SortByNameActive))]
    public partial bool SortByName { get; set; }

    [ObservableProperty]
    public partial bool HasBranches { get; set; }

    [ObservableProperty]
    public partial int LocalCount { get; set; }

    public bool HasSelection    => SelectedBranch != null;
    public bool CanCheckout     => SelectedBranch is { IsCurrent: false };
    public bool CanDelete       => SelectedBranch is { IsCurrent: false, IsRemote: false };
    public bool SortByDateActive => !SortByName;
    public bool SortByNameActive => SortByName;

    public BranchesViewModel(
        IGitBackend              git,
        OperationFeedbackService feedback,
        OperationsLogService     opsLog,
        SnapshotService          snapshots,
        Func<Task>               onChanged)
    {
        _git       = git;
        _feedback  = feedback;
        _opsLog    = opsLog;
        _snapshots = snapshots;
        _onChanged = onChanged;

        GroupedBranches = CollectionViewSource.GetDefaultView(Branches);
        GroupedBranches.GroupDescriptions.Add(new PropertyGroupDescription(nameof(BranchRow.GroupLabel)));
    }

    public async Task LoadAsync()
    {
        try
        {
            var items = await _git.GetBranchListAsync();
            _all = items.Select(b => new BranchRow(b)).ToList();
        }
        catch
        {
            _all = [];
        }

        LocalCount = _all.Count(b => !b.IsRemote);
        Rebuild();
    }

    public void Clear()
    {
        _all = [];
        Branches.Clear();
        SelectedBranch = null;
        HasBranches = false;
        LocalCount = 0;
    }

    partial void OnFilterTextChanged(string value) => Rebuild();
    partial void OnSortByNameChanged(bool value) => Rebuild();

    private void Rebuild()
    {
        var query = FilterText.Trim();

        IEnumerable<BranchRow> filtered = _all;
        if (!string.IsNullOrEmpty(query))
            filtered = filtered.Where(b => b.Name.Contains(query, StringComparison.OrdinalIgnoreCase));

        // Local group first, then Remote; within each group sort by the chosen key.
        var ordered = filtered
            .OrderBy(b => b.GroupOrder)
            .ThenBy(b => SortByName ? 0 : 1)               // keep stable
            .ThenByDescending(b => SortByName ? DateTimeOffset.MinValue : b.LastActivity)
            .ThenBy(b => SortByName ? b.Name : string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var prev = SelectedBranch?.Name;

        Branches.Clear();
        foreach (var b in ordered) Branches.Add(b);

        SelectedBranch = Branches.FirstOrDefault(b => b.Name == prev)
                         ?? Branches.FirstOrDefault(b => b.IsCurrent)
                         ?? Branches.FirstOrDefault();

        HasBranches = Branches.Count > 0;

        // Local-only projection for the title-bar branch picker (current branch first).
        LocalBranches.Clear();
        foreach (var b in _all.Where(b => !b.IsRemote)
                              .OrderByDescending(b => b.IsCurrent)
                              .ThenByDescending(b => b.LastActivity))
            LocalBranches.Add(b);
    }

    [RelayCommand]
    private void SortByDate() => SortByName = false;

    [RelayCommand]
    private void SortByNameToggle() => SortByName = true;

    [RelayCommand(CanExecute = nameof(CanCheckout))]
    private Task Checkout() => SelectedBranch is { } row ? CheckoutRowAsync(row) : Task.CompletedTask;

    /// <summary>Checks out a branch by name (title-bar dropdown, A11). No-op if already current.</summary>
    [RelayCommand]
    private Task CheckoutNamed(string? name)
    {
        var row = _all.FirstOrDefault(b => b.Name == name);
        return row is { IsCurrent: false } ? CheckoutRowAsync(row) : Task.CompletedTask;
    }

    private async Task CheckoutRowAsync(BranchRow row)
    {
        try
        {
            await _feedback.RunAsync("Checkout", () => _git.CheckoutBranchAsync(row.Name));
            await AfterChangeAsync();
        }
        catch (CheckoutConflictException)
        {
            // Dirty working tree would be overwritten — offer stash-and-checkout.
            var choice = MessageBox.Show(
                "You have uncommitted changes that would be overwritten by this checkout.\n\n" +
                "Stash your changes and switch anyway?",
                "Uncommitted changes",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (choice != MessageBoxResult.OK) return;

            try
            {
                await _feedback.RunAsync("Stash & checkout", async () =>
                {
                    await _git.CreateStashAsync($"Auto-stash before checkout of {row.Name}", includeUntracked: true);
                    await _git.CheckoutBranchAsync(row.Name);
                });
                await AfterChangeAsync();
                MessageBox.Show(
                    "Your changes were stashed before switching. Find them in the Stashes view.",
                    "Stashed", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Checkout failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Checkout failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task Rename()
    {
        if (SelectedBranch is not { } row) return;

        var dialog = new TextInputDialog
        {
            Title  = "Rename branch",
            Prompt = $"New name for '{row.Name}':",
            Value  = row.IsRemote ? string.Empty : row.Name,
            Owner  = Application.Current.MainWindow,
        };
        if (dialog.ShowDialog() != true) return;

        var newName = dialog.Value.Trim();
        if (string.IsNullOrEmpty(newName) || newName == row.Name) return;

        try
        {
            _ = _snapshots.CaptureAsync(_git, $"Rename branch {row.Name} → {newName}");
            await _feedback.RunAsync("Rename branch", () => _git.RenameBranchAsync(row.Name, newName));
            await AfterChangeAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Rename failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task Delete()
    {
        if (SelectedBranch is not { } row) return;

        var warn = row.IsMerged
            ? $"Delete branch '{row.Name}'?"
            : $"Branch '{row.Name}' is NOT merged into the current branch.\n\n" +
              "Deleting it may permanently lose its commits. Delete anyway?";

        var confirm = MessageBox.Show(warn, "Delete branch",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            _ = _snapshots.CaptureAsync(_git, $"Delete branch {row.Name}");
            await _feedback.RunAsync("Delete branch", () => _git.DeleteBranchAsync(row.Name, force: !row.IsMerged));
            await AfterChangeAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Delete failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task CreateFromHere()
    {
        if (SelectedBranch is not { } row) return;

        var dialog = new TextInputDialog
        {
            Title  = "Create branch",
            Prompt = $"New branch name (starting from '{row.Name}'):",
            Value  = string.Empty,
            Owner  = Application.Current.MainWindow,
        };
        if (dialog.ShowDialog() != true) return;

        var name = dialog.Value.Trim();
        if (string.IsNullOrEmpty(name)) return;

        try
        {
            _ = _snapshots.CaptureAsync(_git, $"Create branch {name}");
            await _feedback.RunAsync("Create branch", () => _git.CreateBranchAsync(name, row.Info.TipSha));
            await AfterChangeAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Create failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task AfterChangeAsync()
    {
        await LoadAsync();
        await _onChanged();
    }
}
