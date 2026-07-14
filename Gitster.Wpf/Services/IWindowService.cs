using System.Windows;
using Microsoft.Win32;

namespace Gitster.Services;

public interface IWindowService
{
    void SetOwner(Window owner);

    bool? ShowDialog(Window dialog);

    bool? ShowDialog(CommonDialog dialog);

    MessageBoxResult ShowMessage(
        string text,
        string caption,
        MessageBoxButton button = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.None);

    bool Confirm(string text, string caption);

    void Info(string text, string caption = "Gitster");

    void Warning(string text, string caption = "Gitster");

    void Error(string text, string caption = "Gitster");
}
