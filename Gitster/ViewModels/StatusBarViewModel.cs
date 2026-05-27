using System.ComponentModel;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gitster.Models;
using Gitster.Services;

namespace Gitster.ViewModels;

public partial class StatusBarViewModel : BaseViewModel
{
    private readonly RepositoryStateService _stateService;
    private readonly OperationFeedbackService _feedbackService;

    public StatusBarViewModel(RepositoryStateService stateService, OperationFeedbackService feedbackService)
    {
        _stateService = stateService;
        _feedbackService = feedbackService;

        _stateService.PropertyChanged += OnServiceChanged;
        _feedbackService.PropertyChanged += OnServiceChanged;

        RefreshDerivedState();
    }

    [ObservableProperty]
    public partial string StateText { get; set; } = "No repository";

    [ObservableProperty]
    public partial Brush StateIndicatorBrush { get; set; } = Brushes.Gray;

    [ObservableProperty]
    public partial bool HasFeedback { get; set; }

    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    public partial bool IsFailure { get; set; }

    [ObservableProperty]
    public partial string FeedbackText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FeedbackIcon { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Brush FeedbackBrush { get; set; } = Brushes.Gray;

    [ObservableProperty]
    public partial string? RepositoryPath { get; set; }

    [ObservableProperty]
    public partial string RepositoryPathDisplay { get; set; } = "";

    [RelayCommand]
    private void DismissFeedback() => _feedbackService.Dismiss();

    private void OnServiceChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshDerivedState();
    }

    private void RefreshDerivedState()
    {
        RepositoryPath = _stateService.RepositoryPath;
        RepositoryPathDisplay = ToCompactPath(RepositoryPath);

        var branch = _stateService.CurrentBranch;
        switch (_stateService.WorkingTreeState)
        {
            case WorkingTreeState.Clean:
                StateText = string.IsNullOrWhiteSpace(branch) ? "Clean" : $"Clean · {branch}";
                StateIndicatorBrush = ResolveBrush("AccentSuccess", Brushes.ForestGreen);
                break;

            case WorkingTreeState.Dirty dirty:
                StateText = $"{dirty.Modified} modified, {dirty.Staged} staged, {dirty.Untracked} untracked";
                StateIndicatorBrush = ResolveBrush("AccentWarning", Brushes.DarkOrange);
                break;

            case WorkingTreeState.Merging:
                StateText = "MERGING";
                StateIndicatorBrush = ResolveBrush("AccentDanger", Brushes.IndianRed);
                break;

            case WorkingTreeState.Rebasing rebasing:
                StateText = rebasing.TotalSteps > 0
                    ? $"REBASING ({rebasing.CurrentStep}/{rebasing.TotalSteps})"
                    : "REBASING";
                StateIndicatorBrush = ResolveBrush("AccentBlue", Brushes.DodgerBlue);
                break;

            case WorkingTreeState.CherryPicking cherryPicking:
                StateText = $"CHERRY-PICKING {ShortSha(cherryPicking.Sha)}";
                StateIndicatorBrush = ResolveBrush("AccentBlue", Brushes.DodgerBlue);
                break;
        }

        switch (_feedbackService.Current)
        {
            case null:
                HasFeedback = false;
                IsRunning = false;
                IsFailure = false;
                FeedbackText = string.Empty;
                FeedbackIcon = string.Empty;
                FeedbackBrush = ResolveBrush("TextSecondary", Brushes.Gray);
                break;

            case OperationFeedback.Running running:
                HasFeedback = true;
                IsRunning = true;
                IsFailure = false;
                FeedbackIcon = string.Empty;
                FeedbackText = $"{running.Verb}...";
                FeedbackBrush = ResolveBrush("TextSecondary", Brushes.Gray);
                break;

            case OperationFeedback.Success success:
                HasFeedback = true;
                IsRunning = false;
                IsFailure = false;
                FeedbackIcon = "✓";
                FeedbackText = string.IsNullOrWhiteSpace(success.Detail)
                    ? $"{success.Verb} completed"
                    : $"{success.Verb}: {success.Detail}";
                FeedbackBrush = ResolveBrush("AccentSuccess", Brushes.ForestGreen);
                break;

            case OperationFeedback.Failure failure:
                HasFeedback = true;
                IsRunning = false;
                IsFailure = true;
                FeedbackIcon = "!";
                FeedbackText = $"{failure.Verb} failed: {failure.Reason}";
                FeedbackBrush = ResolveBrush("AccentDanger", Brushes.IndianRed);
                break;
        }
    }

    private static string ToCompactPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (path.StartsWith(home, StringComparison.OrdinalIgnoreCase))
            return "~" + path[home.Length..];

        return path;
    }

    private static string ShortSha(string sha)
        => string.IsNullOrEmpty(sha) ? string.Empty : sha[..Math.Min(7, sha.Length)];

    private static Brush ResolveBrush(string key, Brush fallback)
    {
        if (App.Current?.Resources[key] is Brush brush)
            return brush;

        return fallback;
    }
}
