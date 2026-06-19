using System.Windows;
using Microsoft.Win32;

namespace Gitster.Services;

public sealed class WindowService : IWindowService
{
    private Window? _owner;

    public void SetOwner(Window owner)
    {
        _owner = owner;
    }

    public bool? ShowDialog(Window dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);

        var owner = ResolveOwner();
        if (dialog.Owner is null && owner is not null && !ReferenceEquals(dialog, owner))
            dialog.Owner = owner;

        return dialog.ShowDialog();
    }

    public bool? ShowDialog(CommonDialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);

        var owner = ResolveOwner();
        return owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
    }

    public MessageBoxResult ShowMessage(
        string text,
        string caption,
        MessageBoxButton button = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.None)
    {
        var owner = ResolveOwner();
        return owner is null
            ? MessageBox.Show(text, caption, button, image)
            : MessageBox.Show(owner, text, caption, button, image);
    }

    public bool Confirm(string text, string caption)
    {
        return ShowMessage(text, caption, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    public void Info(string text, string caption = "Gitster")
    {
        ShowMessage(text, caption, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public void Warning(string text, string caption = "Gitster")
    {
        ShowMessage(text, caption, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    public void Error(string text, string caption = "Gitster")
    {
        ShowMessage(text, caption, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private Window? ResolveOwner()
    {
        if (_owner is not null)
            return _owner;

        var current = Application.Current;
        return current?.MainWindow;
    }
}
