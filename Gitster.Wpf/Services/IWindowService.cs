using System.Windows;
using Microsoft.Win32;

using Gitster.Core.Ui;

namespace Gitster.Services;

/// <summary>
/// WPF-head window/dialog surface. Extends the framework-neutral <see cref="IUserInteraction"/>
/// (Confirm/Info/Warning/Error/Ask) with the WPF-specific members the head still needs.
/// ViewModels should depend on the narrower ApplicationLayer ports; this stays for the head.
/// </summary>
public interface IWindowService : IUserInteraction
{
    void SetOwner(Window owner);

    bool? ShowDialog(Window dialog);

    bool? ShowDialog(CommonDialog dialog);

    MessageBoxResult ShowMessage(
        string text,
        string caption,
        MessageBoxButton button = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.None);
}
