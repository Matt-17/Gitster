using System.Windows;

using CommunityToolkit.Mvvm.Input;

namespace Gitster.ViewModels;

public partial class QuickActionsViewModel : BaseViewModel
{
    // Reword and Fixup require Git CLI (FixupAutosquash capability).
    // Capability.Requires="FixupAutosquash" on the buttons handles enable/disable.
    [RelayCommand]
    private void Reword() =>
        MessageBox.Show("Reword will be available in Phase 2.", "Coming soon", MessageBoxButton.OK, MessageBoxImage.Information);

    [RelayCommand]
    private void Fixup() =>
        MessageBox.Show("Fixup will be available in Phase 2.", "Coming soon", MessageBoxButton.OK, MessageBoxImage.Information);

    // Cherry-pick and ChangeAuthor work via LibGit2Sharp but are not yet implemented.
    [RelayCommand(CanExecute = nameof(CanCherryPick))]
    private void CherryPick() { }
    private static bool CanCherryPick() => false;

    [RelayCommand(CanExecute = nameof(CanChangeAuthor))]
    private void ChangeAuthor() { }
    private static bool CanChangeAuthor() => false;
}
