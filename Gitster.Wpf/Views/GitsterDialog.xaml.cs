using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Gitster.Services;

namespace Gitster.Views;

/// <summary>
/// The app's message box: same contract as <see cref="MessageBox"/> (buttons, image, result,
/// Esc, Enter, Ctrl+C, system sound), drawn in the Gitster theme.
/// </summary>
public partial class GitsterDialog : Window
{
    private readonly MessageBoxButton _button;
    private readonly MessageBoxImage _image;
    private MessageBoxResult _result;

    public GitsterDialog(
        string text,
        string caption,
        MessageBoxButton button,
        MessageBoxImage image)
    {
        InitializeComponent();

        _button = button;
        _image = image;
        _result = CancelResult(button);

        Title = caption;
        CaptionText.Text = caption;
        MessageText.Text = text;
        ApplyIcon(image);
        BuildButtons(button);

        // Bound on the window so Ctrl+C works wherever focus is inside the dialog.
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, OnCopy));
        PreviewKeyDown += OnPreviewKeyDown;
        Loaded += (_, _) => FocusDefaultButton();
        ContentRendered += OnContentRendered;
    }

    public static MessageBoxResult Show(
        Window? owner,
        string text,
        string caption,
        MessageBoxButton button = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.None)
    {
        var dialog = new GitsterDialog(text, caption, button, image);
        if (owner is not null && !ReferenceEquals(owner, dialog) && owner.IsLoaded)
            dialog.Owner = owner;
        else
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        dialog.ShowDialog();
        return dialog._result;
    }

    /// <summary>The text Ctrl+C copies when nothing is selected, laid out like the Windows message box.</summary>
    internal string BuildClipboardText()
    {
        var buttons = string.Join("   ", ButtonsHost.Children.OfType<Button>().Select(b => b.Content));
        const string rule = "---------------------------";
        return $"{rule}\r\n{CaptionText.Text}\r\n{rule}\r\n{MessageText.Text}\r\n{rule}\r\n{buttons}\r\n{rule}\r\n";
    }

    private void OnContentRendered(object? sender, EventArgs e)
    {
        // A chromeless window shown from a background continuation does not always get
        // activated; without activation no key (Esc, Enter, Ctrl+C) reaches it.
        Activate();
        FocusDefaultButton();
        PlaySystemSound(_image);
    }

    private void FocusDefaultButton()
    {
        var button = ButtonsHost.Children.OfType<Button>().FirstOrDefault(b => b.IsDefault)
            ?? ButtonsHost.Children.OfType<Button>().FirstOrDefault();
        if (button is not null)
            Keyboard.Focus(button);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            // Esc closes like the close button, wherever focus is.
            CloseAsCancelled();
            e.Handled = true;
            return;
        }

        // Ctrl+C (also Ctrl+Insert) is handled here rather than left to the text box: the text
        // box swallows the copy command when nothing is selected.
        var isCopy = (Keyboard.Modifiers & ModifierKeys.Control) != 0
            && (e.Key == Key.C || e.Key == Key.Insert);
        if (isCopy)
        {
            Copy();
            e.Handled = true;
        }
    }

    private void OnCopy(object sender, ExecutedRoutedEventArgs e)
    {
        Copy();
        e.Handled = true;
    }

    private void Copy()
    {
        var text = MessageText.SelectionLength > 0
            ? MessageText.SelectedText
            : BuildClipboardText();
        if (!WpfClipboard.TrySetText(text))
            SystemSounds.Beep.Play();
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
            Margin = new Thickness(8, 0, 0, 0),
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

    private void ApplyIcon(MessageBoxImage image)
    {
        // Several MessageBoxImage members share a value (Error = Hand = Stop,
        // Warning = Exclamation, Information = Asterisk), so the switch covers them all.
        var (shapeKey, glyphKey, colorKey) = image switch
        {
            MessageBoxImage.Error => ("MessageIconCircleGeometry", "MessageIconErrorGlyph", "AccentDanger"),
            MessageBoxImage.Warning => ("MessageIconTriangleGeometry", "MessageIconWarningGlyph", "AccentWarning"),
            MessageBoxImage.Question => ("MessageIconCircleGeometry", "MessageIconQuestionGlyph", "AccentBlue"),
            MessageBoxImage.Information => ("MessageIconCircleGeometry", "MessageIconInfoGlyph", "AccentBlue"),
            _ => (null, null, null),
        };

        if (shapeKey is null)
        {
            // Like the Windows message box: no image, no icon.
            IconHost.Visibility = Visibility.Collapsed;
            return;
        }

        IconShape.SetResourceReference(System.Windows.Shapes.Path.DataProperty, shapeKey);
        IconShape.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, colorKey);
        IconShape.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, colorKey);
        IconGlyph.SetResourceReference(System.Windows.Shapes.Path.DataProperty, glyphKey);
    }

    private static void PlaySystemSound(MessageBoxImage image)
    {
        var sound = image switch
        {
            MessageBoxImage.Error => SystemSounds.Hand,
            MessageBoxImage.Warning => SystemSounds.Exclamation,
            MessageBoxImage.Question => SystemSounds.Question,
            MessageBoxImage.Information => SystemSounds.Asterisk,
            _ => null,
        };
        sound?.Play();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => CloseAsCancelled();

    private void CloseAsCancelled()
    {
        _result = CancelResult(_button);
        DialogResult = false;
    }

    /// <summary>What closing without a button means: OK for a plain OK box, otherwise No or Cancel.</summary>
    private static MessageBoxResult CancelResult(MessageBoxButton button) =>
        button switch
        {
            MessageBoxButton.YesNo => MessageBoxResult.No,
            MessageBoxButton.OK => MessageBoxResult.OK,
            _ => MessageBoxResult.Cancel,
        };
}
