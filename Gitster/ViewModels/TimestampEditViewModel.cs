using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Gitster.Models;

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
            new TimestampPreset("Yesterday 18:00",  () => DateTime.Today.AddDays(-1).AddHours(18)),
            new TimestampPreset("Fri 17:30",        () =>
            {
                var d = DateTime.Today;
                while (d.DayOfWeek != DayOfWeek.Friday) d = d.AddDays(-1);
                return d.Date.AddHours(17).AddMinutes(30);
            }),
        ];
    }

    partial void OnSelectedDateChanged(DateTime? value) =>
        OnPropertyChanged(nameof(PreviewAfter));

    [RelayCommand]
    private void ApplyPreset(TimestampPreset preset) =>
        SelectedDate = preset.Resolve();

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
