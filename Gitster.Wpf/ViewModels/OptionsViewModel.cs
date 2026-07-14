using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Gitster.Services;
using Gitster.Core;

namespace Gitster.ViewModels;

/// <summary>
/// Backs the File ▸ Options dialog: a left category list and a right settings panel.
/// Currently exposes the "Branches" category for editing global favourite branch names.
/// </summary>
public partial class OptionsViewModel : BaseViewModel
{
    private readonly BranchFavoritesService _favorites;
    private readonly UiPreferencesService _ui;

    public OptionsViewModel(BranchFavoritesService favorites, UiPreferencesService ui)
    {
        _favorites = favorites;
        _ui = ui;
        Categories = ["Branches", "Appearance"];
        SelectedCategory = Categories[0];
    }

    public ObservableCollection<string> Categories { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBranchesCategory))]
    [NotifyPropertyChangedFor(nameof(IsAppearanceCategory))]
    public partial string SelectedCategory { get; set; }

    public bool IsBranchesCategory => SelectedCategory == "Branches";
    public bool IsAppearanceCategory => SelectedCategory == "Appearance";

    // ── Appearance category ──────────────────────────────────────────────

    /// <summary>Font family used to render branch/ref names in the commits sidebar.</summary>
    public string BranchFontFamily
    {
        get => _ui.BranchFontFamily;
        set
        {
            if (_ui.BranchFontFamily == value)
                return;
            _ui.BranchFontFamily = string.IsNullOrWhiteSpace(value)
                ? UiPreferencesService.DefaultBranchFontFamily
                : value;
            OnPropertyChanged();
        }
    }

    /// <summary>A few sensible font suggestions; the field stays free-text/editable.</summary>
    public ObservableCollection<string> FontSuggestions { get; } =
    [
        UiPreferencesService.DefaultBranchFontFamily,
        "Segoe UI",
        "Cascadia Mono, Consolas",
        "Consolas",
        "Verdana",
    ];

    // ── Branches category ────────────────────────────────────────────────

    /// <summary>Global favourite branch names — favourited on every repository.</summary>
    public ObservableCollection<string> FavoriteBranchNames => _favorites.GlobalFavorites;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddFavoriteCommand))]
    public partial string NewBranchName { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveFavoriteCommand))]
    public partial string? SelectedFavoriteName { get; set; }

    private bool CanAdd => !string.IsNullOrWhiteSpace(NewBranchName);

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void AddFavorite()
    {
        _favorites.AddGlobalFavorite(NewBranchName);
        NewBranchName = string.Empty;
    }

    private bool CanRemove => !string.IsNullOrWhiteSpace(SelectedFavoriteName);

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private void RemoveFavorite()
    {
        if (SelectedFavoriteName is { } name)
            _favorites.RemoveGlobalFavorite(name);
    }
}
