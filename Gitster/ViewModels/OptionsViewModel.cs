using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Gitster.Services;

namespace Gitster.ViewModels;

/// <summary>
/// Backs the File ▸ Options dialog: a left category list and a right settings panel.
/// Currently exposes the "Branches" category for editing global favourite branch names.
/// </summary>
public partial class OptionsViewModel : BaseViewModel
{
    private readonly BranchFavoritesService _favorites;

    public OptionsViewModel(BranchFavoritesService favorites)
    {
        _favorites = favorites;
        Categories = ["Branches"];
        SelectedCategory = Categories[0];
    }

    public ObservableCollection<string> Categories { get; }

    [ObservableProperty]
    public partial string SelectedCategory { get; set; }

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
