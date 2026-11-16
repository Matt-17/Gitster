using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Gitster.Views;

public partial class GitsterDialog : Window
{
    private MessageBoxResult _result = MessageBoxResult.Cancel;

    public GitsterDialog(
        string text,
        string caption,
        MessageBoxButton button,
        MessageBoxImage image)
    {
        InitializeComponent();

        Title = caption;
        CaptionText.Text = caption;
        MessageText.Text = text;
        ApplyVariant(image);
        BuildButtons(button);
        PreviewKeyDown += OnPreviewKeyDown;

        // Nothing has keyboard focus in a chromeless window by default, so neither the
        // IsCancel access key (Esc) nor Enter reliably reach the buttons. Focus the default one.
        Loaded += (_, _) =>
        {
            Activate();
            ButtonsHost.Children.OfType<Button>().FirstOrDefault(b => b.IsDefault)?.Focus();
        };
    }

    /// <summary>
    /// Ctrl+C copies the dialog like the Windows message box does: caption, message and
    /// buttons, so an error can be pasted into a bug report.
    /// </summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            // Esc always closes, like the close button (No/Cancel result), wherever focus is.
            OnCloseClicked(this, e);
            e.Handled = true;
            return;
        }

        if (e.Key != Key.C || (Keyboard.Modifiers & ModifierKeys.Control) == 0)
            return;

        var buttons = string.Join("   ", ButtonsHost.Children.OfType<Button>().Select(b => b.Content));
        const string rule = "---------------------------";
        var text = $"{rule}\r\n{CaptionText.Text}\r\n{rule}\r\n{MessageText.Text}\r\n{rule}\r\n{buttons}\r\n{rule}\r\n";
        try
        {
            Clipboard.SetText(text);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Clipboard is busy (another app holds it); nothing sensible to do.
        }

        e.Handled = true;
    }

    public static MessageBoxResult Show(
        Window? owner,
        string text,
        string caption,
        MessageBoxButton button = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.None)
    {
        var dialog = new GitsterDialog(text, caption, button, image);
        if (owner is not null && !ReferenceEquals(owner, dialog))
            dialog.Owner = owner;

        return dialog.ShowDialog() == true ? dialog._result : CancelResult(button);
    }

    private void BuildButtons(MessageBoxButton button)
    {
        ButtonsHost.Children.Clear();
        switch (button)
        {
            case MessageBoxButton.OKCancel:
                AddButton("OK", MessageBoxResult.OK, isDefault: true);
                AddButton("Cancel", MessageBoxResult.Cancel, isCancel: true);
                break;
            case MessageBoxButton.YesNo:
                AddButton("Yes", MessageBoxResult.Yes, isDefault: true);
                AddButton("No", MessageBoxResult.No, isCancel: true);
                break;
            case MessageBoxButton.YesNoCancel:
                AddButton("Yes", MessageBoxResult.Yes, isDefault: true);
                AddButton("No", MessageBoxResult.No);
                AddButton("Cancel", MessageBoxResult.Cancel, isCancel: true);
                break;
            default:
                AddButton("OK", MessageBoxResult.OK, isDefault: true, isCancel: true);
                break;
        }
    }

    private void AddButton(
        string text,
        MessageBoxResult result,
        bool isDefault = false,
        bool isCancel = false)
    {
        var button = new Button
        {
            Content = text,
            Margin = new Thickness(6, 0, 0, 0),
            IsDefault = isDefault,
            IsCancel = isCancel,
        };
        button.SetResourceReference(StyleProperty, isDefault ? "DialogPrimaryButton" : "DialogButton");
        button.Click += (_, _) =>
        {
            _result = result;
            DialogResult = true;
        };
        ButtonsHost.Children.Add(button);
    }

    private void ApplyVariant(MessageBoxImage image)
    {
        var icon = image switch
        {
            MessageBoxImage.Warning => "!",
            MessageBoxImage.Error => "x",
            MessageBoxImage.Question => "?",
            _ => "i",
        };
        IconText.Text = icon;

        var backgroundKey = image switch
        {
            MessageBoxImage.Warning => "AccentWarningBackground",
            MessageBoxImage.Error => "AccentDangerBackground",
            MessageBoxImage.Question => "AccentInfoBackground",
            _ => "AccentInfoBackground",
        };
        var foregroundKey = image switch
        {
            MessageBoxImage.Warning => "AccentWarning",
            MessageBoxImage.Error => "AccentDanger",
            MessageBoxImage.Question => "AccentBlue",
            _ => "AccentBlue",
        };

        if (IconText.Parent is Border badge)
            badge.SetResourceReference(Border.BackgroundProperty, backgroundKey);
        IconText.SetResourceReference(ForegroundProperty, foregroundKey);
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        _result = CancelResult(ButtonsHost.Children.OfType<Button>().Any(b => Equals(b.Content, "No"))
            ? MessageBoxButton.YesNo
            : MessageBoxButton.OKCancel);
        DialogResult = false;
        Close();
    }

    private static MessageBoxResult CancelResult(MessageBoxButton button) =>
        button switch
        {
            MessageBoxButton.YesNo => MessageBoxResult.No,
            MessageBoxButton.OK => MessageBoxResult.OK,
            _ => MessageBoxResult.Cancel,
        };
}
