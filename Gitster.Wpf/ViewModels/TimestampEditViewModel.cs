using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Gitster.Core.Models;
using Gitster.Services;
using Gitster.Core;
using Gitster.Services.Features;
using Gitster.Core.Features;

namespace Gitster.ViewModels;

public partial class TimestampEditViewModel : BaseViewModel
{
    private readonly Func<CommitItem?> _getSelectedCommit;
    private readonly Func<DateTime?> _getCurrentCommitDate;

    [ObservableProperty]
    public partial DateTime? SelectedDate { get; set; }

    [ObservableProperty]
    public partial string PreviewBefore { get; set; } = "—";

    public string PreviewAfter =>
        SelectedDate.HasValue ? SelectedDate.Value.ToString("dd.MM. HH:mm") : "—";

    public ObservableCollection<TimestampPreset> Presets { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplySelectedPresetCommand))]
    public partial TimestampPreset? SelectedPreset { get; set; }

    public TimestampEditViewModel(
        Func<CommitItem?> getSelectedCommit,
        Func<DateTime?> getCurrentCommitDate)
    {
        _getSelectedCommit = getSelectedCommit;
        _getCurrentCommitDate = getCurrentCommitDate;

        SelectedDate = DateTime.Now;

        Presets =
        [
            new TimestampPreset("Now",              () => DateTime.Now),
            new TimestampPreset("−1 h",             () => DateTime.Now.AddHours(-1)),
            new TimestampPreset("yesterday 09:00",  () => TimestampPresetResolver.Resolve("yesterday 09:00", DateTime.Now)),
            new TimestampPreset("last Friday 17:30", () => TimestampPresetResolver.Resolve("last Friday 17:30", DateTime.Now)),
        ];
        SelectedPreset = Presets.FirstOrDefault();
    }

    public TimestampEditViewModel(ISelectionContext selectionContext)
        : this(
            () => selectionContext.SelectedCommit,
            () => selectionContext.CurrentCommitDate)
    {
    }

    partial void OnSelectedDateChanged(DateTime? value) =>
        OnPropertyChanged(nameof(PreviewAfter));

    [RelayCommand]
    private void ApplyPreset(TimestampPreset preset) =>
        SelectedDate = preset.Resolve();

    [RelayCommand(CanExecute = nameof(HasSelectedPreset))]
    private void ApplySelectedPreset()
    {
        if (SelectedPreset is not null)
            SelectedDate = SelectedPreset.Resolve();
    }

    private bool HasSelectedPreset() => SelectedPreset is not null;

    [RelayCommand]
    private void ReadFromSelectedCommit()
    {
        var commit = _getSelectedCommit();
        if (commit != null)
            SelectedDate = commit.Date;
    }

    [RelayCommand]
    private void ReadFromCurrentCommit()
    {
        var date = _getCurrentCommitDate();
        if (date.HasValue)
            SelectedDate = date.Value;
    }

    public void UpdatePreviewBefore(string text) => PreviewBefore = text;
}
