using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Gitster.Models;
using Gitster.Services;
using Gitster.Services.Features;
using Gitster.Services.Git;
using Gitster.Services.OperationsLog;
using Gitster.Views;

namespace Gitster.ViewModels;

// ── StashItem ──────────────────────────────────────────────────────────────

/// <summary>UI wrapper around a <see cref="StashInfo"/> entry.</summary>
public class StashItem
{
    private string _userName;

    public StashItem(StashInfo info, string? userName = null)
    {
        Info       = info;
        _userName  = userName ?? string.Empty;
    }

    public StashInfo Info { get; }

    public string UserName
    {
        get => _userName;
        set => _userName = value ?? string.Empty;
    }

    public string DisplayName  => string.IsNullOrEmpty(_userName) ? Info.AutoName : _userName;
    public string Ref          => $"stash@{{{Info.Index}}}";
    public string BranchName   => Info.BranchName;
    public DateTime CreatedAt  => Info.CreatedAt.LocalDateTime;
    public string CommitSha    => Info.CommitSha;

    public IReadOnlyList<StashFileChange> Files => Info.Files;
    public int TotalAdded   => Files.Sum(f => f.Added);
    public int TotalRemoved => Files.Sum(f => f.Removed);

    public string MetaLine =>
        $"{Ref} · {(string.IsNullOrEmpty(BranchName) ? "—" : BranchName)} · +{TotalAdded} −{TotalRemoved}";

    /// <summary>Branch-name slug derived from DisplayName for pre-filling the branch-name input.</summary>
    public string SlugName =>
        Regex.Replace(
            DisplayName.ToLowerInvariant()
                       .Replace(' ', '-')
                       .Replace('/', '-')
                       .Replace('.', '-'),
            @"[^a-z0-9\-]", string.Empty)
        .Trim('-');
}

// ── StashesViewModel ───────────────────────────────────────────────────────

public partial class StashesViewModel : BaseViewModel
{
    private readonly IGitBackend           _git;
    private readonly OperationFeedbackService _feedbackService;
    private readonly OperationsLogService  _opsLogService;
    private readonly SnapshotService       _snapshotService;
    private readonly StashNameService      _nameService;
    private readonly IWindowService        _windowService;
    private readonly Func<Task>            _onStashesChanged;

    private List<StashItem> _allStashes = [];
    private CancellationTokenSource? _diffCts;

    public ObservableCollection<StashItem> Stashes { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedStash))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyCanExecuteChangedFor(nameof(PopCommand))]
    [NotifyCanExecuteChangedFor(nameof(DropCommand))]
    [NotifyCanExecuteChangedFor(nameof(RenameCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConvertToBranchCommand))]
    public partial StashItem? SelectedStash { get; set; }

    [ObservableProperty]
    public partial string FilterText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DiffHeader { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool DiffLoading { get; set; }

    [ObservableProperty]
    public partial List<DiffFileEntry> DiffFiles { get; set; } = [];

    [ObservableProperty]
    public partial bool HasStashes { get; set; }

    public bool HasSelectedStash => SelectedStash != null;

    public string DiffHeaderDisplay =>
        DiffLoading ? "loading..."
        : SelectedStash == null ? "no stash selected"
        : string.IsNullOrEmpty(DiffHeader) ? "no diff to display"
        : DiffHeader;

    partial void OnDiffHeaderChanged(string value) => OnPropertyChanged(nameof(DiffHeaderDisplay));

    partial void OnDiffLoadingChanged(bool value) => OnPropertyChanged(nameof(DiffHeaderDisplay));

    // ── Constructor ────────────────────────────────────────────────────────

    public StashesViewModel(
        IGitBackend git,
        OperationFeedbackService feedbackService,
        OperationsLogService opsLogService,
        SnapshotService snapshotService,
        StashNameService nameService,
        IWindowService? windowService,
        RepositoryCommandContext commandContext)
        : this(
            git,
            feedbackService,
            opsLogService,
            snapshotService,
            nameService,
            windowService,
            commandContext.RefreshSidebarBadges)
    {
    }

    public StashesViewModel(
        IGitBackend git,
        OperationFeedbackService feedbackService,
        OperationsLogService opsLogService,
        SnapshotService snapshotService,
        StashNameService nameService,
        IWindowService? windowService,
        Func<Task> onStashesChanged)
    {
        _git              = git;
        _feedbackService  = feedbackService;
        _opsLogService    = opsLogService;
        _snapshotService  = snapshotService;
        _nameService      = nameService;
        _windowService    = windowService ?? new WindowService();
        _onStashesChanged = onStashesChanged;
    }

    // ── Load ───────────────────────────────────────────────────────────────

    public async Task LoadAsync()
    {
        try
        {
            var infos   = await _git.GetStashesAsync();
            _allStashes = infos.Select(s => new StashItem(s, _nameService.GetName(s.CommitSha))).ToList();
        }
        catch
        {
            _allStashes = [];
        }

        ApplyFilter();
        HasStashes = _allStashes.Count > 0;
    }

    public void Clear()
    {
        _allStashes = [];
        Stashes.Clear();
        SelectedStash = null;
        _diffCts?.Cancel();
        DiffFiles     = [];
        DiffHeader    = string.Empty;
        DiffLoading   = false;
        HasStashes    = false;
    }

    // ── Filter ─────────────────────────────────────────────────────────────

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var query = FilterText.Trim();

        var filtered = string.IsNullOrEmpty(query)
            ? _allStashes
            : _allStashes.Where(s =>
                s.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                s.BranchName.Contains(query, StringComparison.OrdinalIgnoreCase)  ||
                s.Files.Any(f => f.Path.Contains(query, StringComparison.OrdinalIgnoreCase))).ToList();

        // Preserve selection if possible
        var prev = SelectedStash;

        Stashes.Clear();
        foreach (var s in filtered)
            Stashes.Add(s);

        SelectedStash = Stashes.FirstOrDefault(s => s.CommitSha == prev?.CommitSha)
                        ?? Stashes.FirstOrDefault();
    }

    partial void OnSelectedStashChanged(StashItem? value)
    {
        OnPropertyChanged(nameof(DiffHeaderDisplay));
        if (value != null)
            _ = LoadDiffAsync(value);
        else
        {
            _diffCts?.Cancel();
            DiffFiles = [];
            DiffHeader = string.Empty;
            DiffLoading = false;
        }
    }

    private async Task LoadDiffAsync(StashItem stash)
    {
        _diffCts?.Cancel();
        var cts = new CancellationTokenSource();
        _diffCts = cts;
        var token = cts.Token;

        DiffFiles = [];
        DiffHeader = string.Empty;
        DiffLoading = true;

        try
        {
            var diff = await Task.Run(() => _git.GetStashDiffAsync(stash.Info.Index, token), token);
            if (token.IsCancellationRequested || !IsCurrentSelection(stash))
                return;

            DiffFiles = diff.Files.ToList();
            DiffHeader = string.IsNullOrEmpty(diff.Header)
                ? stash.Ref
                : $"{stash.Ref} - {diff.Header}";
            DiffLoading = false;
        }
        catch (OperationCanceledException)
        {
            // Selection changed.
        }
        catch
        {
            if (!IsCurrentSelection(stash))
                return;

            DiffFiles = [];
            DiffHeader = string.Empty;
            DiffLoading = false;
        }
    }

    private bool IsCurrentSelection(StashItem stash) =>
        SelectedStash?.CommitSha == stash.CommitSha
        && SelectedStash.Info.Index == stash.Info.Index;

    // ── Commands ───────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task NewStash()
    {
        var dialog = new NewStashDialog();
        if (_windowService.ShowDialog(dialog) != true) return;

        try
        {
            await _snapshotService.CaptureAsync(_git, "Create stash");
            await _feedbackService.RunAsync("Stash",
                () => _git.CreateStashAsync(dialog.StashMessage, dialog.IncludeUntracked));
            await LoadAsync();
            await _onStashesChanged();
        }
        catch (Exception ex)
        {
            _windowService.Warning(ex.Message, "Stash failed");
        }
    }

    private bool CanActOnStash() => SelectedStash != null;

    [RelayCommand(CanExecute = nameof(CanActOnStash))]
    private async Task Apply()
    {
        if (SelectedStash is not { } stash) return;
        await _snapshotService.CaptureAsync(_git, $"Apply {stash.Ref}");
        try
        {
            await _feedbackService.RunAsync("Apply stash",
                () => _git.ApplyStashAsync(stash.Info.Index));
            await _onStashesChanged();
        }
        catch (Exception ex)
        {
            if (await ConflictGuidanceService.ShowIfConflictAsync(_windowService, _git, "Apply stash", ex))
                return;

            _windowService.Warning(ex.Message, "Apply failed");
        }
    }

    [RelayCommand(CanExecute = nameof(CanActOnStash))]
    private async Task Pop()
    {
        if (SelectedStash is not { } stash) return;

        await _snapshotService.CaptureAsync(_git, $"Pop {stash.Ref}");

        try
        {
            await _feedbackService.RunAsync("Pop stash",
                () => _git.PopStashAsync(stash.Info.Index));

            var shortSha = stash.CommitSha.Length >= 7 ? stash.CommitSha[..7] : stash.CommitSha;
            await _opsLogService.RecordAsync(new OperationRecord(
                Id:              Guid.NewGuid().ToString(),
                Timestamp:       DateTimeOffset.Now,
                Kind:            OperationKind.StashPop,
                Description:     $"Pop {stash.Ref}: {stash.DisplayName}",
                BranchName:      stash.BranchName,
                BeforeSha:       shortSha,
                AfterSha:        string.Empty,
                ReflogSelector:  null,
                Status:          OperationStatus.Active));

            await LoadAsync();
            await _onStashesChanged();
        }
        catch (Exception ex)
        {
            if (await ConflictGuidanceService.ShowIfConflictAsync(_windowService, _git, "Pop stash", ex))
                return;

            _windowService.Warning(ex.Message, "Pop failed");
        }
    }

    [RelayCommand(CanExecute = nameof(CanActOnStash))]
    private async Task Drop()
    {
        if (SelectedStash is not { } stash) return;

        var confirm = _windowService.ShowMessage(
            $"Drop \"{stash.DisplayName}\" ({stash.Ref})?\n\n" +
            "Dropped stashes cannot be recovered from the reflog easily.",
            "Drop stash",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        await _snapshotService.CaptureAsync(_git, $"Drop {stash.Ref}");

        try
        {
            await _feedbackService.RunAsync("Drop stash",
                () => _git.DropStashAsync(stash.Info.Index));

            await _nameService.RemoveAsync(stash.CommitSha);

            var shortSha = stash.CommitSha.Length >= 7 ? stash.CommitSha[..7] : stash.CommitSha;
            await _opsLogService.RecordAsync(new OperationRecord(
                Id:              Guid.NewGuid().ToString(),
                Timestamp:       DateTimeOffset.Now,
                Kind:            OperationKind.StashDrop,
                Description:     $"Drop {stash.Ref}: {stash.DisplayName}",
                BranchName:      stash.BranchName,
                BeforeSha:       shortSha,
                AfterSha:        string.Empty,
                ReflogSelector:  null,
                Status:          OperationStatus.Active));

            await LoadAsync();
            await _onStashesChanged();
        }
        catch (Exception ex)
        {
            _windowService.Warning(ex.Message, "Drop failed");
        }
    }

    [RelayCommand(CanExecute = nameof(CanActOnStash))]
    private async Task Rename()
    {
        if (SelectedStash is not { } stash) return;

        var dialog = new TextInputDialog
        {
            Title  = "Rename stash",
            Prompt = "New name (leave empty to reset to auto-name):",
            Value  = stash.UserName,
        };
        if (_windowService.ShowDialog(dialog) != true) return;

        await _nameService.SetNameAsync(stash.CommitSha, dialog.Value.Trim());
        await LoadAsync(); // rebuilds display names
    }

    [RelayCommand(CanExecute = nameof(CanActOnStash))]
    private async Task ConvertToBranch()
    {
        if (SelectedStash is not { } stash) return;

        var dialog = new TextInputDialog
        {
            Title  = "Convert stash to branch",
            Prompt = "New branch name:",
            Value  = stash.SlugName,
        };
        if (_windowService.ShowDialog(dialog) != true) return;

        var branchName = dialog.Value.Trim();
        if (string.IsNullOrEmpty(branchName))
        {
            _windowService.Warning("Branch name cannot be empty.", "Convert to branch");
            return;
        }

        await _snapshotService.CaptureAsync(_git, $"Convert {stash.Ref} to branch '{branchName}'");

        try
        {
            await _feedbackService.RunAsync("Convert to branch",
                () => _git.ConvertStashToBranchAsync(stash.Info.Index, branchName));

            var shortSha = stash.CommitSha.Length >= 7 ? stash.CommitSha[..7] : stash.CommitSha;
            await _opsLogService.RecordAsync(new OperationRecord(
                Id:              Guid.NewGuid().ToString(),
                Timestamp:       DateTimeOffset.Now,
                Kind:            OperationKind.StashConvert,
                Description:     $"Convert {stash.Ref} → branch '{branchName}'",
                BranchName:      branchName,
                BeforeSha:       shortSha,
                AfterSha:        string.Empty,
                ReflogSelector:  null,
                Status:          OperationStatus.Active));

            await LoadAsync();
            await _onStashesChanged();

            _windowService.Info(
                $"Created branch '{branchName}' from {stash.Ref} and applied the stash.\n\n" +
                "You are now on the new branch.",
                "Branch created");
        }
        catch (Exception ex)
        {
            if (await ConflictGuidanceService.ShowIfConflictAsync(_windowService, _git, "Convert stash", ex))
                return;

            _windowService.Warning(ex.Message, "Convert failed");
        }
    }
}
