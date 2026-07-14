using System.Windows;
using Gitster.Views;
using Microsoft.Win32;

using Gitster.Core;
using Gitster.Core.Ui;

namespace Gitster.Services;

public sealed class WindowService : IWindowService, IUserInteraction
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
        return GitsterDialog.Show(ResolveOwner(), text, caption, button, image);
    }

    public bool Confirm(string text, string caption)
    {
        return ShowMessage(text, caption, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    // IUserInteraction: the WPF-free prompt used by ViewModels. Maps neutral enums to the styled
    // GitsterDialog and back, so callers never see a System.Windows type.
    public MessageResult Ask(string text, string caption, MessageButtons buttons = MessageButtons.Ok, MessageIcon icon = MessageIcon.None)
    {
        var wpfButton = buttons switch
        {
            MessageButtons.OkCancel => MessageBoxButton.OKCancel,
            MessageButtons.YesNo => MessageBoxButton.YesNo,
            MessageButtons.YesNoCancel => MessageBoxButton.YesNoCancel,
            _ => MessageBoxButton.OK,
        };
        var wpfImage = icon switch
        {
            MessageIcon.Information => MessageBoxImage.Information,
            MessageIcon.Warning => MessageBoxImage.Warning,
            MessageIcon.Error => MessageBoxImage.Error,
            MessageIcon.Question => MessageBoxImage.Question,
            _ => MessageBoxImage.None,
        };
        return ShowMessage(text, caption, wpfButton, wpfImage) switch
        {
            MessageBoxResult.Yes => MessageResult.Yes,
            MessageBoxResult.No => MessageResult.No,
            MessageBoxResult.Cancel => MessageResult.Cancel,
            _ => MessageResult.Ok,
        };
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
